using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib.Subscriptions;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using System.Security.Claims;

namespace Onepunch.Auth.Core.Services
{
    public class InvitationService : BaseService<Invitation>
    {
        private readonly EmailTokenService _emailTokenService;
        private readonly TenantRequestService _tenantCreationRequestStatusService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly Domains _domains;
        private readonly UserManager<User> _manager;
        private readonly IPublishEndpoint _publisher;
        private readonly JwtService _jwtService;
        private readonly MembershipCacheService _membershipCacheService;
        private readonly MembershipGrpcClient _membershipGrpcClient;
        private readonly OnePunch.Auth.Core.Auditing.AuthAuditRecorder? _audit;

        // audit is optional so the direct constructions in tests stay valid; the container supplies it.
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
            MembershipCacheService membershipCacheService,
            MembershipGrpcClient membershipGrpcClient,
            OnePunch.Auth.Core.Auditing.AuthAuditRecorder? audit = null) : base(uow)
        {
            _audit = audit;
            _membershipGrpcClient = membershipGrpcClient;
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

            if (!InvitationPolicy.CanInvite(userClaim, user))
                throw new UnauthorizedException();

            // A single invite comes from the Employee list, so it's an employee invite.
            var roles = payload.Roles.Count > 0
                ? payload.Roles.Distinct().ToList()
                : [InvitationPolicy.DefaultRoleFor(InviteEntityType.Employee)];
            foreach (var role in roles)
            {
                if (InvitationPolicy.RoleError(role) is { } error)
                    throw new GuardException($"Invalid role '{role}'. {error}");
            }
            await EnsureMayGrantRolesAsync(userClaim, user.Id, tenantId, roles, InviteEntityType.Employee);

            // Block re-inviting someone who already has an active membership on this tenant --
            // sending another invitation would just create a redundant Pending row for someone
            // who can already sign in; if the goal is to change what they can do, that's a role
            // edit on their existing membership (Security > Users), not a new invitation. A
            // still-pending invitation doesn't block here: a single invite is also how an admin
            // resends one (CreateInvitationAsync supersedes the old link).
            var check = (await CheckRecipientsAsync([payload.Email], tenantId, ct)).Single();
            if (check.Status == InviteRecipientStatus.Member)
                throw new GuardException("This person is already a member of your organization. Change their role from the Users page instead of sending a new invitation.");

            // An admin invitation takes one of the plan's admin seats; a resend doesn't take another.
            if (check.Status != InviteRecipientStatus.PendingInvite && InvitationPolicy.TakesAdminSeat(roles))
                await _membershipGrpcClient.EnsurePlanAllowsAsync(EntitlementKeys.AdminsMax, userId: null, tenantId);

            await EnsureProvisionedAsync(tenantId);

            await CreateInvitationAsync(user, tenantId, tenantName,
                new BatchInvitationRecipient { Email = payload.Email, EntityId = payload.EmployeeId, Name = payload.Name },
                roles, clientId: null, ct);
            await CommitChangesAsync(ct);
        }

        /// <summary>
        /// The default role for the kind of invite (Employee / Client) needs nothing more. Any
        /// other role needs "Users:AssignRole", and even then only one the inviter could grant
        /// themselves -- Tenant Service knows what each role grants, so it decides.
        /// </summary>
        private async Task EnsureMayGrantRolesAsync(
            ClaimsPrincipal userClaim, Guid callerId, Guid tenantId, IEnumerable<string> roles, InviteEntityType entityType)
        {
            var chosen = roles.Where(r => !InvitationPolicy.IsDefaultRole(r, entityType)).ToList();
            if (chosen.Count == 0) return;
            if (!InvitationPolicy.CanAssignRole(userClaim))
                throw new GuardException(InvitationPolicy.AssignRoleDenied);
            // The Owner may grant any role (Tenant Service would say the same).
            if (InvitationPolicy.IsOwner(userClaim)) return;

            var (allowed, error) = await _membershipGrpcClient.CheckRoleGrantAsync(callerId, tenantId, chosen);
            if (!allowed)
                throw new GuardException(error ?? "You can't grant that role.");
        }

        /// <summary>
        /// For each email, what inviting it now would run into: not an email, already an active
        /// member of this company, or already holding a still-valid invitation to it. Drives the
        /// batch modal's live chip states and is re-checked when a batch is actually sent.
        /// </summary>
        public async Task<List<InviteRecipientCheck>> CheckRecipientsAsync(IEnumerable<string> emails, Guid tenantId, CancellationToken ct)
        {
            var normalized = emails.Select(InvitationPolicy.Normalize).Distinct().Take(InvitationPolicy.MaxBatchSize).ToList();
            var valid = normalized.Where(InvitationPolicy.IsValidEmail).ToList();

            var now = DateTime.UtcNow;
            var pending = (await GetQueryable(x =>
                    valid.Contains(x.Email) &&
                    x.Category == InvitationCategory.Staff &&
                    x.TenantId == tenantId &&
                    x.Status == InvitationStatus.Pending &&
                    x.Expiry > now)
                .Select(x => new { x.Email, x.CreatedAt })
                .ToListAsync(ct))
                .GroupBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(x => x.CreatedAt), StringComparer.OrdinalIgnoreCase);

            var results = new List<InviteRecipientCheck>();
            foreach (var email in normalized)
            {
                if (!InvitationPolicy.IsValidEmail(email))
                {
                    results.Add(new InviteRecipientCheck { Email = email, Status = InviteRecipientStatus.InvalidEmail });
                    continue;
                }

                // Only "Active" counts -- a Revoked/Inactive member regaining access through a
                // fresh invite is fine. Scoped to this company: a member of another one is exactly
                // who invitations are for.
                var account = await _manager.FindByEmailAsync(email);
                if (account != null)
                {
                    var memberships = await _membershipCacheService.GetMembershipsAsync(account.Id);
                    if (memberships.Any(m => m.TenantId == tenantId && m.State == "Active"))
                    {
                        results.Add(new InviteRecipientCheck
                        {
                            Email = email,
                            Status = InviteRecipientStatus.Member,
                            Name = account.FullName ?? account.Email,
                        });
                        continue;
                    }
                }

                results.Add(pending.TryGetValue(email, out var sentAt)
                    ? new InviteRecipientCheck { Email = email, Status = InviteRecipientStatus.PendingInvite, SentAt = sentAt }
                    : new InviteRecipientCheck { Email = email, Status = InviteRecipientStatus.Ok });
            }
            return results;
        }

        /// <summary>
        /// Invites up to 100 people in one go, all with one role: employees (each email tied to its
        /// employee) or logins for one client. Every recipient is checked again here, since the
        /// modal's live check can be stale; ones that can't be invited are reported, the rest are
        /// sent. A repeated <paramref name="idempotencyKey"/> returns the first result without
        /// sending anything again.
        /// </summary>
        public async Task<BatchInvitationResult> SendBatchAsync(
            BatchInvitationRequest payload,
            Guid tenantId,
            string tenantName,
            string idempotencyKey,
            ClaimsPrincipal userClaim,
            CancellationToken ct)
        {
            var user = await _manager.GetUserAsync(userClaim);
            if (user == null) throw new UnauthorizedException();
            Guard.ThrowIfEmpty(tenantId, "tenant");
            if (!InvitationPolicy.CanInvite(userClaim, user)) throw new UnauthorizedException();

            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
                throw new GuardException("A valid X-Idempotency-Key header is required.");
            var previous = await Context.InvitationBatches.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey, ct);
            if (previous != null)
                return System.Text.Json.JsonSerializer.Deserialize<BatchInvitationResult>(previous.ResultJson) ?? new();

            if (InvitationPolicy.RoleError(payload.Role) is { } roleError) throw new GuardException(roleError);
            await EnsureMayGrantRolesAsync(userClaim, user.Id, tenantId, [payload.Role.Trim()], payload.EntityType);
            if (payload.Recipients.Count == 0) throw new GuardException("Add at least one email address.");
            if (payload.Recipients.Count > InvitationPolicy.MaxBatchSize)
                throw new GuardException($"A batch can invite at most {InvitationPolicy.MaxBatchSize} people.");
            if (payload.EntityType == InviteEntityType.Client && (payload.ClientId ?? Guid.Empty) == Guid.Empty)
                throw new GuardException("Choose the client these logins are for.");

            await EnsureProvisionedAsync(tenantId);

            var recipients = payload.Recipients
                .Select(r => new BatchInvitationRecipient { Email = InvitationPolicy.Normalize(r.Email), EntityId = r.EntityId, Name = r.Name?.Trim() })
                .DistinctBy(r => r.Email)
                .ToList();
            var checks = (await CheckRecipientsAsync(recipients.Select(r => r.Email), tenantId, ct))
                .ToDictionary(c => c.Email, StringComparer.OrdinalIgnoreCase);

            var result = new BatchInvitationResult();
            var sent = new List<BatchInvitationRecipient>();
            var roles = new List<string> { payload.Role.Trim() };
            var clientId = payload.EntityType == InviteEntityType.Client ? payload.ClientId : null;

            string? ReasonToSkip(BatchInvitationRecipient recipient) => checks[recipient.Email].Status switch
            {
                InviteRecipientStatus.InvalidEmail => "Invalid email format.",
                InviteRecipientStatus.Member => "Already an active member of this company.",
                InviteRecipientStatus.PendingInvite => "Already has a pending invitation.",
                _ => payload.EntityType == InviteEntityType.Employee && recipient.EntityId is null
                    ? "No employee record is linked to this email."
                    : null,
            };

            // Admin invitations take the plan's admin seats -- the whole batch fits or none is sent.
            if (InvitationPolicy.TakesAdminSeat(roles))
            {
                var seats = recipients.Count(r => ReasonToSkip(r) == null);
                if (seats > 0)
                    await _membershipGrpcClient.EnsurePlanAllowsAsync(EntitlementKeys.AdminsMax, userId: null, tenantId, seats);
            }

            foreach (var recipient in recipients)
            {
                var reason = ReasonToSkip(recipient);
                if (reason != null)
                {
                    result.Failed.Add(new FailedInvitation { Email = recipient.Email, Reason = reason });
                    continue;
                }

                await CreateInvitationAsync(user, tenantId, tenantName,
                    payload.EntityType == InviteEntityType.Employee ? recipient : new BatchInvitationRecipient { Email = recipient.Email, Name = recipient.Name },
                    roles, clientId, ct);
                sent.Add(recipient);
            }
            result.Sent = sent.Count;

            Context.InvitationBatches.Add(new InvitationBatch
            {
                TenantId = tenantId,
                IdempotencyKey = idempotencyKey,
                InvitedByUserId = user.Id,
                ResultJson = System.Text.Json.JsonSerializer.Serialize(result),
            });
            if (_audit != null && sent.Count > 0)
            {
                await _audit.InvitationBatchSentAsync(user, tenantId, payload.EntityType, payload.Role, clientId, sent, ct);
            }
            await CommitChangesAsync(ct);
            return result;
        }

        private async Task EnsureProvisionedAsync(Guid tenantId)
        {
            var request = await _tenantCreationRequestStatusService.FindByTenant(tenantId);
            if (request != null && request.Status == TenantCreationStatus.Provisioning)
                throw new GuardException("Your organization is still being provisioned. Please try again shortly.");
        }

        /// <summary>
        /// Creates one invitation (not committed): expires any still-valid one for the same email
        /// and company, then publishes the invite email and Tenant Service's pending membership.
        /// Shared by the single and batch paths so both send exactly the same invitation.
        /// </summary>
        private async Task CreateInvitationAsync(
            User inviter,
            Guid tenantId,
            string tenantName,
            BatchInvitationRecipient recipient,
            List<string> roles,
            Guid? clientId,
            CancellationToken ct)
        {
            // Supersede any still-pending invitations for this email/tenant instead of blocking
            // a resend. Expiring the old ones (rather than leaving them valid alongside the new
            // one) closes the "stale token still works" hygiene gap. noTracking: false -- Expiry
            // is mutated directly below, so the entities must be tracked for it to persist.
            var priorPending = await GetQueryable(x =>
                x.Email == recipient.Email &&
                x.Category == InvitationCategory.Staff &&
                x.TenantId == tenantId &&
                x.Status == InvitationStatus.Pending &&
                x.Expiry > DateTime.UtcNow, false)
                .ToListAsync(ct);
            foreach (var prior in priorPending)
            {
                prior.Expiry = DateTime.UtcNow;
            }

            var token = _emailTokenService.GetRandomToken;
            var exp = DateTime.UtcNow.Add(InvitationPolicy.LinkLifetime);
            var name = string.IsNullOrWhiteSpace(recipient.Name) ? null : recipient.Name;

            Repository.Add(new Invitation
            {
                Email = recipient.Email,
                TenantId = tenantId,
                TenantName = tenantName,
                UserId = inviter.Id,
                Expiry = exp,
                Token = token,
                Status = InvitationStatus.Pending,
                Roles = roles,
                EmployeeId = recipient.EntityId,
                ClientId = clientId,
                Name = name,
            });
            var frontEndHost = (_domains.FrontEnd ?? "").TrimEnd('/');
            await _publisher.Publish(new UserInvitionNotificationPayload
            {
                Email = recipient.Email,
                InviteLink = $"{frontEndHost}/accept-invite?token={token}",
                Organization = tenantName ?? inviter.DefaultTenantName ?? "",
                Name = name ?? recipient.Email,
                Expiry = exp,
            }, ct);

            // Fire-and-forget alongside the email publish above (not gated on it) so Tenant
            // Service can create a pending membership row before the invitee ever accepts.
            await _publisher.Publish(new UserInvited
            {
                Email = recipient.Email,
                TenantId = tenantId,
                TenantName = tenantName ?? "",
                Roles = roles,
                InvitedByUserId = inviter.Id,
                FullName = name ?? recipient.Email,
                InvitationToken = token,
                Expiry = exp,
                ClientId = clientId,
            }, ct);
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
            var invitation = await GetQueryable(x => x.Token == invitationToken && x.Category == InvitationCategory.Staff, false).FirstOrDefaultAsync(ct);
            if (invitation == null)
                throw new GuardException("This invitation link is invalid.", "INVITATION_INVALID");

            // Must be verified before anything else — otherwise any authenticated user holding
            // a valid token for someone else's invite (forwarded email, shared link, etc.) could
            // join that tenant under their own account.
            if (!string.Equals(invitation.Email, user.Email, StringComparison.OrdinalIgnoreCase))
                throw new GuardException("This invitation was sent to a different email address.", "INVITATION_EMAIL_MISMATCH");

            if (invitation.Status == InvitationStatus.Accepted)
            {
                // Idempotent replay: the same invited user already accepted this token (e.g. the
                // frontend's auto-accept effect double-firing, a reload, or back/forward
                // navigation back onto the accept-invite page). Re-mint a tenant-scoped token
                // without re-publishing UserJoin/InvitationAccepted/UserOnboarded, which already
                // fired once and could be double-processed downstream. Only while the membership
                // is still Active -- a member revoked since must not get back in through this link.
                var replayActivation = await ActivateOrThrowAsync(user, invitation, ActivationMode.Replay);
                return await MintTenantScopedResponseAsync(user, invitation, replayActivation, ct);
            }

            if (invitation.Expiry <= DateTime.UtcNow)
                throw new GuardException("This invitation has expired. Ask whoever invited you to send a new one.", "INVITATION_EXPIRED");

            return await FinalizeAcceptanceAsync(user, invitation, ct);
        }

        /// <summary>
        /// Flow B step 6 "Clicks invite link": lets the accept-invite landing page render who
        /// invited them and to which workspace before asking for any credentials.
        /// </summary>
        public async Task<InvitationPreviewResponse?> GetPreviewAsync(string invitationToken, CancellationToken ct = default)
        {
            var invitation = await GetQueryable(x => x.Token == invitationToken && x.Category == InvitationCategory.Staff)
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
            var invitation = await GetQueryable(x => x.Token == invitationToken && x.Category == InvitationCategory.Staff, false).FirstOrDefaultAsync(ct);
            if (invitation == null)
                throw new GuardException("This invitation link is invalid.", "INVITATION_INVALID");

            // Checked before the invitation's own status — it's the most actionable message
            // regardless of whether the invitation itself is still pending, already accepted, or
            // expired: if an account exists, the answer is always "sign in instead".
            var existingUser = await _manager.FindByEmailAsync(invitation.Email);
            if (existingUser != null)
                throw new GuardException("An account already exists for this email. Please sign in — you'll be prompted to accept this invitation automatically.", "INVITATION_ACCOUNT_EXISTS");

            if (invitation.Status == InvitationStatus.Accepted)
                throw new GuardException("This invitation has already been used.", "INVITATION_ALREADY_USED");

            if (invitation.Expiry <= DateTime.UtcNow)
                throw new GuardException("This invitation has expired. Ask whoever invited you to send a new one.", "INVITATION_EXPIRED");

            var user = new User
            {
                UserName = invitation.Email,
                Email = invitation.Email,
                FullName = string.IsNullOrWhiteSpace(name) ? invitation.Email.Split('@')[0] : name,
                Status = "Active",
                EmailConfirmed = true,
            }.BeganAs(AccountOrigins.StaffInvite, DateTime.UtcNow, invitation.Id.ToString());
            // The invitation email proved the address, and accepting signs them straight in.
            user.StampEmailConfirmed(user.CreatedAtUtc!.Value);
            user.StampFirstHrisSignIn(user.CreatedAtUtc.Value);
            var createResult = await _manager.CreateAsync(user, password);
            if (!createResult.Succeeded)
                throw new GuardException(string.Join(" ", createResult.Errors.Select(e => e.Description)));

            try
            {
                return await FinalizeAcceptanceAsync(user, invitation, ct);
            }
            catch (GuardException)
            {
                // The invite was revoked (or Tenant Service couldn't confirm it) after the
                // account was created above -- don't leave an unused, signed-in-able account behind.
                await _manager.DeleteAsync(user);
                throw;
            }
        }

        /// <summary>
        /// Shared tail of both Accept paths: marks the invitation accepted, publishes UserJoin /
        /// InvitationAccepted / UserOnboarded, and mints a token scoped to the newly-joined
        /// tenant. The membership itself is activated synchronously inside
        /// MintTenantScopedResponseAsync (gRPC ActivateMembership) so the token already carries
        /// its roles/permissions; the events stay for their other downstream consumers and are
        /// idempotent against that activation — diagram step 13 "Enter workspace, no
        /// provisioning wait" since the tenant itself already exists and is fully provisioned.
        /// </summary>
        private async Task<LoginResponse> FinalizeAcceptanceAsync(User user, Invitation invitation, CancellationToken ct)
        {
            // Asked first, before the invitation is marked used or any event is published: if an
            // admin revoked/removed the invite, Tenant Service refuses and nothing is left behind.
            var activation = await ActivateOrThrowAsync(user, invitation, ActivationMode.AcceptInvite);

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

            await _publisher.Publish(new UserOnboarded
            {
                UserId = user.Id,
                TenantId = invitation.TenantId,
                EmployeeId = invitation.EmployeeId,
                ClientId = invitation.ClientId,
                Email = invitation.Email,
            }, (PublishContext<UserOnboarded> ctx) => ctx.Headers.Set("X-Tenant-ID", targetTenantId), ct);

            return await MintTenantScopedResponseAsync(user, invitation, activation, ct);
        }

        /// <summary>
        /// Activates the membership on Tenant Service SYNCHRONOUSLY, and refuses (fails closed) when
        /// the invite was revoked/removed or Tenant Service can't confirm it -- minting a token from
        /// the invitation's own roles on a failed call would let a revoked invite through.
        /// </summary>
        private async Task<MembershipResolveResult> ActivateOrThrowAsync(User user, Invitation invitation, ActivationMode mode)
        {
            var activation = await _membershipGrpcClient.ActivateMembershipAsync(
                user.Id,
                invitation.TenantId,
                invitation.TenantName,
                invitation.Email,
                user.FullName ?? user.Email,
                invitation.Roles,
                mode);
            if (activation.Denied)
                throw new GuardException("This invitation is no longer valid. Ask whoever invited you to send a new one.", "INVITATION_REVOKED");
            if (!activation.Success)
                throw new GuardException("We couldn't confirm your invitation right now. Please try again in a moment.", "INVITATION_UNAVAILABLE");
            return activation;
        }

        // Shared tail of both a fresh acceptance (FinalizeAcceptanceAsync, after flipping
        // Status and publishing events) and an idempotent replay of an already-accepted
        // invitation (Accept) — mints a tenant-scoped access/refresh token and builds the
        // LoginResponse. The replay path deliberately calls this directly, skipping the
        // Status flip/event publishing above since those already happened once.
        private async Task<LoginResponse> MintTenantScopedResponseAsync(
            User user, Invitation invitation, MembershipResolveResult activation, CancellationToken ct)
        {
            // `activation` comes from the SYNCHRONOUS Tenant Service call made by the caller
            // (ActivateOrThrowAsync), before anything is minted. The UserJoin/InvitationAccepted
            // events the caller published go through the EF bus outbox, so they only leave this
            // service after CommitChangesAsync below -- i.e. after the token is already minted.
            // Relying on them meant the token and response never had this tenant's permissions
            // (and the JWT had no role claims at all), which left the frontend's permission guards
            // redirecting the invitee in an endless loop. Idempotent on Tenant Service's side
            // (UserMembershipService.JoinAsync), so the replay path and the events arriving later
            // are both harmless.

            // Drop any snapshot cached before this membership existed (an earlier login, or
            // before the invite was even sent) so the lookup below reads it fresh -- same
            // reasoning as UserService.RefreshLogin's identical invalidate-before-read.
            await _membershipCacheService.InvalidateAsync(user.Id);
            var tenants = await _membershipCacheService.GetMembershipsAsync(user.Id);
            var joinedWasMissing = tenants.All(t => t.TenantId != invitation.TenantId);
            var joinedTenant = ResolveJoinedTenant(tenants, invitation, activation);
            if (joinedWasMissing)
            {
                // GetMembershipsAsync just cached a snapshot without this tenant (activation
                // degraded, or the lookup itself fell back) -- invalidate again so the next read
                // (token refresh, reload, another tab) retries Tenant Service live instead of
                // serving that incomplete snapshot for the full 5-minute TTL.
                await _membershipCacheService.InvalidateAsync(user.Id);
            }

            // Roles/permissions come straight from the resolution above rather than from
            // CreateTokenAsync's own independent membership lookup, so the JWT's claims always
            // match the LoginResponse body exactly.
            var accessToken = await _jwtService.CreateTokenAsync(
                user,
                invitation.TenantId.ToString(),
                invitation.TenantName ?? "",
                joinedTenant.Roles,
                joinedTenant.Permissions);
            var refreshTokenString = await _jwtService.GenerateRefreshToken();

            user.DefaultTenantId = invitation.TenantId;
            user.DefaultTenantName = joinedTenant.Name;
            user.DefaultTenantRoles = joinedTenant.Roles;

            await Context.RefreshTokens.AddAsync(new RefreshToken
            {
                UserId = user.Id,
                RefreshTokenHash = _jwtService.Hash(refreshTokenString),
                Expiry = DateTime.UtcNow.AddDays(_jwtService.RefreshExpiry),
                CreatedAt = DateTime.UtcNow,
                Revoked = false,
                // This session works in the company just joined -- see RefreshToken.TenantId.
                TenantId = invitation.TenantId,
                TenantName = joinedTenant.Name,
            }, ct);

            await CommitChangesAsync(ct);

            return new LoginResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshTokenString,
                Expiry = DateTime.UtcNow.AddMinutes(_jwtService.TokenExpiry),
                Tenants = tenants,
                Name = user.FullName,
                Roles = joinedTenant.Roles,
                Permissions = joinedTenant.Permissions,
                Email = user.Email,
            };
        }

        /// <summary>
        /// Picks the joined tenant's entry out of the user's membership list (appending an
        /// optimistic one if the lookup didn't include it), then overlays Tenant Service's
        /// authoritative roles/permissions from the synchronous activation whenever it
        /// succeeded. Without a successful activation, the entry keeps whatever the lookup
        /// knew, or the invitation's own roles with no permissions as a last resort -- the next
        /// token refresh corrects it once the async UserJoin event lands. Mutates and returns
        /// the entry inside <paramref name="tenants"/>.
        /// </summary>
        public static UsersTenant ResolveJoinedTenant(
            List<UsersTenant> tenants,
            Invitation invitation,
            MembershipResolveResult activation)
        {
            var joinedTenant = tenants.FirstOrDefault(t => t.TenantId == invitation.TenantId);
            if (joinedTenant == null)
            {
                joinedTenant = new UsersTenant
                {
                    TenantId = invitation.TenantId,
                    Name = invitation.TenantName ?? "",
                    Roles = invitation.Roles,
                    State = "Active",
                    // Invites can only be sent once the tenant has finished provisioning (see
                    // SendUserInvitationAsync's Provisioning guard).
                    HrDbReady = true,
                };
                tenants.Add(joinedTenant);
            }

            if (activation.Success && activation.Found)
            {
                joinedTenant.Roles = activation.Roles;
                joinedTenant.Permissions = activation.Permissions;
                if (!string.IsNullOrEmpty(activation.Status)) joinedTenant.State = activation.Status;
                if (string.IsNullOrEmpty(joinedTenant.Name)) joinedTenant.Name = activation.TenantName;
            }

            return joinedTenant;
        }

        /// <summary>
        /// An admin revoked/removed the pending invite for this email (Tenant Service's
        /// InviteRevoked): expires every still-valid Staff invitation for it, so the link stops
        /// working and the accept-invite page shows it as invalid straight away.
        /// </summary>
        public async Task ExpirePendingAsync(Guid tenantId, string email, CancellationToken ct = default)
        {
            var pending = await GetQueryable(x =>
                x.Email == email &&
                x.Category == InvitationCategory.Staff &&
                x.TenantId == tenantId &&
                x.Status == InvitationStatus.Pending &&
                x.Expiry > DateTime.UtcNow, false)
                .ToListAsync(ct);
            if (pending.Count == 0) return;
            foreach (var invitation in pending) invitation.Expiry = DateTime.UtcNow;
            await CommitChangesAsync(ct);
        }

        public async Task<bool> IsValidAsync(string invitationToken, CancellationToken token = default)
        {
            var result = await GetQueryable(x =>
                x.Token == invitationToken &&
                x.Category == InvitationCategory.Staff &&
                x.Status == InvitationStatus.Pending &&
                x.Expiry > DateTime.UtcNow)
                .FirstOrDefaultAsync(token);
            return result != null;
        }

        public async Task<List<Invitation>> GetPendingByEmailAsync(string email, CancellationToken token = default)
        {
            return await GetQueryable(x =>
                x.Email == email &&
                x.Category == InvitationCategory.Staff &&
                x.Status == InvitationStatus.Pending &&
                x.Expiry > DateTime.UtcNow)
                .ToListAsync(token);
        }
    }
}
