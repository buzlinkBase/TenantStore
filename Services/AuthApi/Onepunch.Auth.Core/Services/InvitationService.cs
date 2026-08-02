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
        private static readonly string[] AllowedInviteRoles = ["Admin", "Member"];

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

            // Prevent duplicate pending invitations
            //var existing = await GetQueryable(x =>
            //    x.Email == payload.Email &&
            //    x.TenantId == tenantId &&
            //    x.Status == InvitationStatus.Pending &&
            //    x.Expiry > DateTime.UtcNow)
            //    .FirstOrDefaultAsync(ct);
            //if (existing != null)
            //    throw new GuardException("An active invitation already exists for this email.");

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
            };
            Repository.Add(invitation);

            // Diagram Flow B step 6 "Clicks invite link": this must land the invitee on the
            // frontend's accept-invite page (which previews the invite and creates/links their
            // account), not a bare backend API route.
            var frontEndHost = (_domains.FrontEnd ?? "").TrimEnd('/');

            await _publisher.Publish(new UserInvitionNotificationPayload
            {
                Email = payload.Email,
                InviteLink = $"{frontEndHost}/accept-invite?token={token}",
                Organization = tenantName ?? user.DefaultTenantName ?? "",
                Name = user.FullName ?? user.Email ?? "User",
                Expiry = exp,
            }, ct);

            // Fire-and-forget alongside the email publish above (not gated on it) so Tenant
            // Service can create a pending membership row before the invitee ever accepts.
            await _publisher.Publish(new UserInvited
            {
                Email = payload.Email,
                TenantId = tenantId,
                TenantName = tenantName,
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

            var invitation = await GetQueryable(x =>
                x.Token == invitationToken &&
                x.Status == InvitationStatus.Pending &&
                x.Expiry > DateTime.UtcNow)
                .FirstOrDefaultAsync(ct);

            if (invitation == null)
                throw new GuardException("Invitation is invalid or has already been used.");

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
            var invitation = await GetQueryable(x =>
                x.Token == invitationToken &&
                x.Status == InvitationStatus.Pending &&
                x.Expiry > DateTime.UtcNow)
                .FirstOrDefaultAsync(ct);

            if (invitation == null)
                throw new GuardException("Invitation is invalid or has already been used.");

            var existingUser = await _manager.FindByEmailAsync(invitation.Email);
            if (existingUser != null)
                throw new GuardException("An account already exists for this email. Please sign in — you'll be prompted to accept this invitation automatically.");

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

            await _publisher.Publish(new UserJoin
            {
                UserId = user.Id,
                TenantId = invitation.TenantId,
                TenantName = invitation.TenantName ?? string.Empty,
                Roles = invitation.Roles,
                Email = invitation.Email,
            }, ct);

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
