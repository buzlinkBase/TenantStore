using MassTransit;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

public class  UserJoinWorker : IConsumer<UserJoin>
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

        await _userMembershipService.AddAsync(new UserMembership
        {
            TenantId = message.TenantId,
            UserId = message.UserId,
            Role = string.IsNullOrWhiteSpace(message.Role) ? "Member" : message.Role,
        }, context.CancellationToken);

        await _publisher.Publish(new UserJoinToTenantPayload
        {
            TenantId = message.TenantId,
            TenantName = message.TenantName,
            UserId = message.UserId,
        });
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }
}
