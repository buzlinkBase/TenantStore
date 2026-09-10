using MassTransit;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

public class UserJoinWorker : IConsumer<UserJoin>
{
    private readonly TenantService _tenantService;
    private readonly UserMembershipService _userMembershipService;
    private readonly IPublishEndpoint _publisher;

    public UserJoinWorker(TenantService tenantService,
        UserMembershipService userMembershipService,
        IPublishEndpoint publisher)
    {
        _tenantService = tenantService;
        _userMembershipService = userMembershipService;
        _publisher = publisher;
    }

    public async Task Consume(ConsumeContext<UserJoin> context)
    {
        var message = context.Message;
        var existing = await _userMembershipService.GetMemberAsync(message.UserId, message.TenantId, context.CancellationToken);
        if (existing != null)
        {
            await _publisher.Publish(new UserJoinToTenantPayload
            {
                TenantId = message.TenantId,
                TenantName = message.TenantName,
                UserId = message.UserId,
                Roles = existing.RoleNames(),
            });
            return;
        }

        var roles = message.Roles.Count > 0 ? message.Roles : ["Member"];

        // Flow B: if an invite placeholder (Status="Invited") is waiting for this email,
        // activate it in place instead of creating a duplicate membership row.
        var pendingInvite = !string.IsNullOrWhiteSpace(message.Email)
            ? await _userMembershipService.FindPendingInviteAsync(message.TenantId, message.Email, context.CancellationToken)
            : null;

        if (pendingInvite != null)
        {
            await _userMembershipService.ActivateInviteAsync(pendingInvite, message.UserId, context.CancellationToken);
            roles = pendingInvite.RoleNames();
        }
        else
        {
            await _userMembershipService.AddAsync(new UserMembership
            {
                TenantId = message.TenantId,
                InvitedEmail = message.Email,
                FullName = message.FullName,
                UserId = message.UserId,
            }, roles, context.CancellationToken);
        }

        await _publisher.Publish(new UserJoinToTenantPayload
        {
            TenantId = message.TenantId,
            TenantName = message.TenantName,
            UserId = message.UserId,
            Roles = roles,
        });
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }
}
