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
        private readonly EmailTokenService _emailTokenService;
        private readonly TenantRequestService _tenantCreationRequestStatusService;
        private readonly Domains _domains;
        private readonly UserManager<User> _manager;
        private readonly IPublishEndpoint publisher;
        public InvitationService(IUnitOfWorkService uow,
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
            this.publisher = publisher;
        }

        public async Task SendUserInvitationAsync(InvitationRequest payload,
            Guid tenantId,
            string tenantName,
            ClaimsPrincipal userClaim, CancellationToken ct)
        {
            var user = await _manager.GetUserAsync(userClaim);
            if (user == null) throw new UnauthorizedException();
            Guard.ThrowIfEmpty(tenantId, "tenant");
            Guard.ThrowIfEmpty(payload.Email, "email");

            var request = await _tenantCreationRequestStatusService.FindOne(tenantId);
            if (request != null && request.Status == TenantCreationStatus.Pending)
            {
                throw new Exception("Your organization is still being provisioned or is inactive.");
            }

            var exp = DateTime.UtcNow.AddDays(1);
            var token = _emailTokenService.GetRandomToken;
            var invitation = new Invitation
            {
                Email = payload.Email,
                TenantId = tenantId,
                UserId = user.Id,
                Expiry = exp,
                Token = token
            };
            Repository.Add(invitation);
            var message = new UserInvitionNotificationPayload
            {
                Email = payload.Email,
                InviteLink = $"{_domains.BaseUrl}/invitations-list?token={token}",
                Organization = tenantName ?? user.DefaultTenantName ?? "",
                Name = user.FullName ?? user.Email ?? "User",
                Expiry = exp,
            };
            await publisher.Publish(message, ct);
            await CommitChangesAsync(ct);
        }

        public async Task Accept(string invitationToken, ClaimsPrincipal userClaim,
            CancellationToken token)
        {
            var user = await _manager.GetUserAsync(userClaim);
            if (user == null) throw new UnauthorizedException();

            var invRequest = await GetQueryable(x => x.Token == invitationToken && x.Expiry > DateTime.UtcNow)
                .FirstOrDefaultAsync(token);

            if (invRequest == null) throw new Exception("Invitation token is expired");
            var joinRequest = new UserJoin
            {
                UserId = user.Id,
                TenantId = invRequest.TenantId,
            };
            await publisher.Publish(joinRequest);
            Context.Invitations.Remove(invRequest);
            await CommitChangesAsync(token);
        }

        public async Task<bool> IsValidAsync(string invitationToken, CancellationToken token = default)
        {
            var result = await GetQueryable(x => x.Token == invitationToken && x.Expiry < DateTime.UtcNow)
                .FirstOrDefaultAsync(token);
            return result != null;
        }

    }
}
