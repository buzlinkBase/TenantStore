using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
        private readonly EmailTokenService _emailTokenService;
        private readonly TenantRequestService _tenantCreationRequestStatusService;
        private readonly Domains _domains;
        private readonly UserManager<User> _manager;
        private readonly IPublishEndpoint _publisher;

        public InvitationService(
            IUnitOfWorkService uow,
            EmailTokenService emailTokenService,
            TenantRequestService tenantCreationRequestStatusService,
            IOptions<Domains> domains,
            IHttpContextAccessor contextAccessor,
            UserManager<User> manager,
            IPublishEndpoint publisher) : base(uow)
        {
            _emailTokenService = emailTokenService;
            _tenantCreationRequestStatusService = tenantCreationRequestStatusService;
            _domains = domains.Value;
            _manager = manager;
            _publisher = publisher;
        }

        public async Task SendUserInvitationAsync(
            InvitationRequest payload,
            Guid tenantId,
            string tenantName,
            ClaimsPrincipal userClaim,
            CancellationToken ct)
        {
            var user = await _manager.GetUserAsync(userClaim);
            if (user == null) throw new UnauthorizedException();
            Guard.ThrowIfEmpty(tenantId, "tenant");
            Guard.ThrowIfEmpty(payload.Email, "email");

            var request = await _tenantCreationRequestStatusService.FindOne(tenantId);
            if (request != null && request.Status == TenantCreationStatus.Pending)
                throw new Exception("Your organization is still being provisioned or is inactive.");

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
                Role = string.IsNullOrWhiteSpace(payload.Role) ? "Member" : payload.Role,
            };
            Repository.Add(invitation);

            await _publisher.Publish(new UserInvitionNotificationPayload
            {
                Email = payload.Email,
                InviteLink = $"{_domains.BaseUrl}/invitations-list?token={token}",
                Organization = tenantName ?? user.DefaultTenantName ?? "",
                Name = user.FullName ?? user.Email ?? "User",
                Expiry = exp,
            }, ct);

            await CommitChangesAsync(ct);
        }

        public async Task Accept(string invitationToken, ClaimsPrincipal userClaim, CancellationToken token)
        {
            var user = await _manager.GetUserAsync(userClaim);
            if (user == null) throw new UnauthorizedException();

            var invitation = await GetQueryable(x =>
                x.Token == invitationToken &&
                x.Status == InvitationStatus.Pending &&
                x.Expiry > DateTime.UtcNow)
                .FirstOrDefaultAsync(token);

            if (invitation == null) throw new Exception("Invitation is invalid or has already been used.");

            invitation.Status = InvitationStatus.Accepted;

            await _publisher.Publish(new UserJoin
            {
                UserId = user.Id,
                TenantId = invitation.TenantId,
                TenantName = invitation.TenantName ?? string.Empty,
                Role = invitation.Role,
            });

            await CommitChangesAsync(token);
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
