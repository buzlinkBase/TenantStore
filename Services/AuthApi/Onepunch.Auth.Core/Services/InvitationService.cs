using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using System.Security.Claims;

namespace Onepunch.Auth.Core.Services
{
    public class InvitationService : BaseService<Invitation>
    {
        // Mirrors tenant-api's current System Roles minus Owner (Owner is never invitable). This
        // is a hardcoded snapshot, not the live source of truth -- tenant-api's RoleService owns
        // the real catalog (System Roles + this tenant's Custom Roles, see
        // RoleService.IsAssignableAsync in TenantStoreApi.Core). A tenant-defined Custom Role will
        // still be rejected here even though tenant-api would accept it. Ideally this list should
        // be replaced with a live cross-service check instead of a hardcoded array kept in sync by
        // hand.
        private static readonly string[] AllowedInviteRoles = ["Admin", "Member", "Employee"];

        private readonly EmailTokenService _emailTokenService;
        private readonly TenantRequestService _tenantCreationRequestStatusService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly Domains _domains;
        private readonly UserManager<User> _manager;
        private readonly IPublishEndpoint _publisher;
        private readonly JwtService _jwtService;
        private readonly MembershipCacheService _membershipCacheService;

        public InvitationService(
            IUnitOfWorkService uow,
            EmailTokenService emailTokenService,
            TenantRequestService tenantCreationRequestStatusService,
            IOptions<Domains> domains,
            IHttpContextAccessor contextAccessor,
            IHttpContextAccessor httpContextAccessor,
            UserManager<User> manager,
            IPublishEndpoint publisher,
            JwtService jwtService,
            MembershipCacheService membershipCacheService) : base(uow)
        {
            _emailTokenService = emailTokenService;
            _tenantCreationRequestStatusService = tenantCreationRequestStatusService;
            _httpContextAccessor = httpContextAccessor;
            _domains = domains.Value;
            _manager = manager;
            _publisher = publisher;
            _jwtService = jwtService;
            _membershipCacheService = membershipCacheService;
        }

        public async Task SendUserInvitationAsync(
            InvitationRequest payload,
            Guid tenantId,
            string tenantName,
            string apiHost,
            ClaimsPrincipal userClaim,
            CancellationToken ct)
        {
            var user = await _manager.GetUserAsync(userClaim);
            if (user == null) throw new UnauthorizedException();

            Guard.ThrowIfEmpty(tenantId, "tenant");
            Guard.ThrowIfEmpty(payload.Email, "email");

            // Caller must be Owner or Admin of this tenant
            if (!user.DefaultTenantRoles.Contains("Owner") && !user.DefaultTenantRoles.Contains("Admin"))
                throw new UnauthorizedException();

            // Validate role values
            var roles = payload.Roles.Count > 0 ? payload.Roles.Distinct().ToList() : ["Member"];
            foreach (var role in roles)
            {
                if (!AllowedInviteRoles.Contains(role))
                    throw new GuardException($"Invalid role '{role}'. Allowed values: {string.Join(", ", AllowedInviteRoles)}.");
            }

            // Tenant must not be pending provisioning
            var request = await _tenantCreationRequestStatusService.FindByTenant(tenantId);
            if (request != null && request.Status == TenantCreationStatus.Provisioning)
                throw new GuardException("Your organization is still being provisioned. Please try again shortly.");

            // Supersede any still-pending invitations for this email/tenant instead of blocking
            // a resend — there's no separate "resend" action, so a hard duplicate guard would be
            // a dead end for the inviting admin. Expiring the old ones (rather than leaving them
            // valid alongside the new one) closes the "stale token still works" hygiene gap.
            // noTracking: false — Expiry is mutated directly below, so the entities must be
            // tracked for that mutation to actually persist on commit.
            var priorPending = await GetQueryable(x =>
                x.Email == payload.Email &&
                x.TenantId == tenantId &&
                x.Status == InvitationStatus.Pending &&
                x.Expiry > DateTime.UtcNow, false)
                .ToListAsync(ct);
            foreach (var prior in priorPending)
            {
                prior.Expiry = DateTime.UtcNow;
            }

            var token = _emailTokenService.GetRandomToken;
            var exp = DateTime.UtcNow.AddDays(1);

            var invitation = new Invitation
            {
                Email = payload.Email,
                TenantId = tenantId,
                TenantName = tenantName,
                UserId = user.Id,
                Expiry = exp,
                Token = token,
                Status = InvitationStatus.Pending,
                Roles = roles,
                EmployeeId = payload.EmployeeId,
                Name = payload.Name,
            };
            Repository.Add(invitation);

            // Diagram Flow B step 6 "Clicks invite link": this must land the invitee on the
            // frontend's accept-invite page (which previews the invite and creates/links their
            // account), not a bare backend API route.
            var frontEndHost = (_domains.FrontEnd ?? "").TrimEnd('/');

            // Prefer the invited employee's actual name (sent by the inviter, who already has
            // it from picking them off the Employee list) -- falls back to their email the same
            // way EmailNotificationService itself falls back when Name comes through blank
            // (e.g. inviting a bare email address with no linked Employee record).
            await _publisher.Publish(new UserInvitionNotificationPayload
            {
                Email = payload.Email,
                InviteLink = $"{frontEndHost}/accept-invite?token={token}",
                Organization = tenantName ?? user.DefaultTenantName ?? "",
                Name = string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name,
                Expiry = exp,
            }, ct);

            // Fire-and-forget alongside the email publish above (not gated on it) so Tenant
            // Service can create a pending membership row before the invitee ever accepts.
            await _publisher.Publish(new UserInvited
            {
                Email = payload.Email,
                TenantId = tenantId,
                TenantName = tenantName ?? "",
                Roles = roles,
                InvitedByUserId = user.Id,
                InvitationToken = token,
                Expiry = exp,
            }, ct);
            await CommitChangesAsync(ct);
        }

        /// <summary>
        /// Diagram steps 10-13: for an already-authenticated user who has a pending invitation
        /// waiting for their email — marks it accepted, activates membership, and re-issues a
        /// token scoped to the newly-joined tenant so they can enter it immediately (their
        /// current token is still scoped to whatever tenant they were on before).
        /// </summary>
        public async Task<LoginResponse> Accept(string invitationToken, ClaimsPrincipal userClaim, CancellationToken ct)
        {
            var user = await _manager.GetUserAsync(userClaim);
            if (user == null) throw new UnauthorizedException();

            // noTracking: false — Status gets mutated directly below (FinalizeAcceptanceAsync /
            // the idempotent-replay branch) rather than going through Repository.AddOrUpdate, so
            // the entity must be tracked for that mutation to actually persist on commit.
            var invitation = await GetQueryable(x => x.Token == invitationToken, false).FirstOrDefaultAsync(ct);
            if (invitation == null)
                throw new GuardException("This invitation link is invalid.");

            // Must be verified before anything else — otherwise any authenticated user holding
            // a valid token for someone else's invite (forwarded email, shared link, etc.) could
            // join that tenant under their own account.
            if (!string.Equals(invitation.Email, user.Email, StringComparison.OrdinalIgnoreCase))
                throw new GuardException("This invitation was sent to a different email address.");

            if (invitation.Status == InvitationStatus.Accepted)
            {
                // Idempotent replay: the same invited user already accepted this token (e.g. the
                // frontend's auto-accept effect double-firing, a reload, or back/forward
                // navigation back onto the accept-invite page). Re-mint a tenant-scoped token
                // without re-publishing UserJoin/InvitationAccepted/UserOnboarded, which already
                // fired once and could be double-processed downstream.
                return await MintTenantScopedResponseAsync(user, invitation, ct);
            }

            if (invitation.Expiry <= DateTime.UtcNow)
                throw new GuardException("This invitation has expired. Ask whoever invited you to send a new one.");

            return await FinalizeAcceptanceAsync(user, invitation, ct);
        }

        /// <summary>
        /// Flow B step 6 "Clicks invite link": lets the accept-invite landing page render who
        /// invited them and to which workspace before asking for any credentials.
        /// </summary>
        public async Task<InvitationPreviewResponse?> GetPreviewAsync(string invitationToken, CancellationToken ct = default)
        {
            var invitation = await GetQueryable(x => x.Token == invitationToken)
                .FirstOrDefaultAsync(ct);
            if (invitation == null) return null;

            var valid = invitation.Status == InvitationStatus.Pending && invitation.Expiry > DateTime.UtcNow;
            var accountExists = await _manager.FindByEmailAsync(invitation.Email) != null;

            return new InvitationPreviewResponse
            {
                Email = invitation.Email,
                TenantName = invitation.TenantName ?? "",
                Roles = invitation.Roles,
                Expiry = invitation.Expiry,
                Valid = valid,
                AccountExists = accountExists,
                EmployeeId = invitation.EmployeeId,
                Name = invitation.Name,
            };
        }

        /// <summary>
        /// Flow B steps 7-9: creates the invited user's account and issues a tenant-scoped
        /// token in one step, for an email with no existing account — "skips provisioning
        /// entirely" since the tenant already exists. Existing accounts must sign in normally
        /// instead (<see cref="Accept"/> already picks up pending invitations by email once
        /// authenticated) — this endpoint deliberately never checks a password against an
        /// arbitrary email, to avoid turning it into a guessing oracle.
        /// </summary>
        public async Task<LoginResponse> AcceptByTokenAsync(
            string invitationToken,
            string? name,
            string password,
            CancellationToken ct)
        {
            // noTracking: false — see the matching comment in Accept().
            var invitation = await GetQueryable(x => x.Token == invitationToken, false).FirstOrDefaultAsync(ct);
            if (invitation == null)
                throw new GuardException("This invitation link is invalid.");

            // Checked before the invitation's own status — it's the most actionable message
            // regardless of whether the invitation itself is still pending, already accepted, or
            // expired: if an account exists, the answer is always "sign in instead".
            var existingUser = await _manager.FindByEmailAsync(invitation.Email);
            if (existingUser != null)
                throw new GuardException("An account already exists for this email. Please sign in — you'll be prompted to accept this invitation automatically.");

            if (invitation.Status == InvitationStatus.Accepted)
                throw new GuardException("This invitation has already been used.");

            if (invitation.Expiry <= DateTime.UtcNow)
                throw new GuardException("This invitation has expired. Ask whoever invited you to send a new one.");

            var user = new User
            {
                UserName = invitation.Email,
                Email = invitation.Email,
                FullName = string.IsNullOrWhiteSpace(name) ? invitation.Email.Split('@')[0] : name,
                Status = "Active",
                EmailConfirmed = true,
            };
            var createResult = await _manager.CreateAsync(user, password);
            if (!createResult.Succeeded)
                throw new GuardException(string.Join(" ", createResult.Errors.Select(e => e.Description)));

            return await FinalizeAcceptanceAsync(user, invitation, ct);
        }

        /// <summary>
        /// Shared tail of both Accept paths: marks the invitation accepted, publishes UserJoin
        /// (Tenant Service activates/creates the membership async off this), and mints a token
        /// scoped to the newly-joined tenant. The membership activation hasn't necessarily landed
        /// by the time we read it back here, so the tenant is appended to the list optimistically
        /// (mirrors WorkspaceService.Create) — diagram step 13 "Enter workspace, no provisioning
        /// wait" since the tenant itself already exists and is already fully provisioned.
        /// </summary>
        private async Task<LoginResponse> FinalizeAcceptanceAsync(User user, Invitation invitation, CancellationToken ct)
        {
            invitation.Status = InvitationStatus.Accepted;

            // The caller's own ambient tenant (whatever TenantPublishFilter would otherwise stamp
            // from their current JWT) is irrelevant here -- they're joining invitation.TenantId,
            // which may differ from (or not exist on) their current session. Every message below
            // is about that target tenant, so the X-Tenant-ID header must say so explicitly, or a
            // tenant-scoped consumer downstream (e.g. hrms-api's UserOnboardedWorker, gated by
            // HrmsContext's automatic tenant filter) silently resolves against the wrong tenant.
            var targetTenantId = invitation.TenantId.ToString();

            await _publisher.Publish(new UserJoin
            {
                UserId = user.Id,
                FullName = user.FullName ?? user.Email ?? "",
                TenantId = invitation.TenantId,
                TenantName = invitation.TenantName ?? string.Empty,
                Roles = invitation.Roles,
                Email = invitation.Email,
            }, (PublishContext<UserJoin> ctx) => ctx.Headers.Set("X-Tenant-ID", targetTenantId), ct);

            await _publisher.Publish(new InvitationAccepted
            {
                UserId = user.Id,
                TenantId = invitation.TenantId,
                Email = invitation.Email,
                Roles = invitation.Roles,
            }, (PublishContext<InvitationAccepted> ctx) => ctx.Headers.Set("X-Tenant-ID", targetTenantId), ct);

            if (invitation.EmployeeId.HasValue)
            {
                await _publisher.Publish(new UserOnboarded
                {
                    UserId = user.Id,
                    TenantId = invitation.TenantId,
                    EmployeeId = invitation.EmployeeId,
                    Email = invitation.Email,
                }, (PublishContext<UserOnboarded> ctx) => ctx.Headers.Set("X-Tenant-ID", targetTenantId), ct);
            }

            return await MintTenantScopedResponseAsync(user, invitation, ct);
        }

        // Shared tail of both a fresh acceptance (FinalizeAcceptanceAsync, after flipping
        // Status and publishing events) and an idempotent replay of an already-accepted
        // invitation (Accept) — mints a tenant-scoped access/refresh token and builds the
        // LoginResponse. The replay path deliberately calls this directly, skipping the
        // Status flip/event publishing above since those already happened once.
        private async Task<LoginResponse> MintTenantScopedResponseAsync(User user, Invitation invitation, CancellationToken ct)
        {
            var accessToken = await _jwtService.CreateTokenAsync(user, invitation.TenantId.ToString(), invitation.TenantName ?? "");
            var refreshTokenString = await _jwtService.GenerateRefreshToken();
            await Context.RefreshTokens.AddAsync(new RefreshToken
            {
                UserId = user.Id,
                RefreshTokenHash = _jwtService.Hash(refreshTokenString),
                Expiry = DateTime.UtcNow.AddDays(_jwtService.RefreshExpiry),
                CreatedAt = DateTime.UtcNow,
                Revoked = false
            }, ct);

            await CommitChangesAsync(ct);

            // UserJoin (published above by the caller) is what actually activates the
            // membership on Tenant Service's side, asynchronously -- but this user may already
            // have a cached membership snapshot from before that (from an earlier login, or
            // from before this invite was even sent), and MembershipCacheService's 5-minute TTL
            // has no trigger tied to this event. Without invalidating first, GetMembershipsAsync
            // below can keep serving that pre-existing stale list, and — since nothing else in
            // this app's silent-refresh path ever re-invalidates on its own — a user who never
            // fully logs out again could see that stale list indefinitely. Same reasoning as
            // UserService.RefreshLogin's identical invalidate-before-read.
            await _membershipCacheService.InvalidateAsync(user.Id);
            var tenants = await _membershipCacheService.GetMembershipsAsync(user.Id);
            if (!tenants.Any(t => t.TenantId == invitation.TenantId))
            {
                tenants.Add(new UsersTenant
                {
                    TenantId = invitation.TenantId,
                    Name = invitation.TenantName ?? "",
                    Roles = invitation.Roles,
                    State = "Active",
                    HrDbReady = true,
                });
            }

            return new LoginResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshTokenString,
                Expiry = DateTime.UtcNow.AddMinutes(_jwtService.TokenExpiry),
                Tenants = tenants,
                Name = user.FullName,
                Roles = invitation.Roles,
                Email = user.Email,
            };
        }

        public async Task<bool> IsValidAsync(string invitationToken, CancellationToken token = default)
        {
            var result = await GetQueryable(x =>
                x.Token == invitationToken &&
                x.Status == InvitationStatus.Pending &&
                x.Expiry > DateTime.UtcNow)
                .FirstOrDefaultAsync(token);
            return result != null;
        }

        public async Task<List<Invitation>> GetPendingByEmailAsync(string email, CancellationToken token = default)
        {
            return await GetQueryable(x =>
                x.Email == email &&
                x.Status == InvitationStatus.Pending &&
                x.Expiry > DateTime.UtcNow)
                .ToListAsync(token);
        }
    }
}
