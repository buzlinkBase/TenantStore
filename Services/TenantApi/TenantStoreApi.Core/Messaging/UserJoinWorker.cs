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

        // Auth only publishes UserJoin after its synchronous ActivateMembership call succeeded, so
        // the membership already exists and is Active. Only confirm that (replay mode): an admin
        // may have revoked or removed the member since, and this late event must neither
        // reactivate a revoked membership nor recreate a removed one.
        var (membership, _) = await _userMembershipService.AcceptInviteAsync(
            message.UserId,
            message.TenantId,
            message.TenantName,
            message.Email,
            message.FullName,
            message.Roles,
            replay: true,
            context.CancellationToken);
        if (membership == null) return;

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
