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

        // Idempotent -- for invitation acceptance, Auth's synchronous ActivateMembership gRPC
        // call has usually already activated this membership by the time this event arrives,
        // in which case JoinAsync just hands back the existing row.
        var membership = await _userMembershipService.JoinAsync(
            message.UserId,
            message.TenantId,
            message.TenantName,
            message.Email,
            message.FullName,
            message.Roles,
            context.CancellationToken);

        await _publisher.Publish(new UserJoinToTenantPayload
        {
            TenantId = message.TenantId,
            TenantName = message.TenantName,
            UserId = message.UserId,
            Roles = membership.RoleNames(),
        });
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }
}
