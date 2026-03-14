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

        //no tenant just a member
        await _userMembershipService.AddAsync(new UserMembership
        {
            TenantId = message.TenantId,
            UserId = message.UserId,
            Role = "Member"
        }, context.CancellationToken);

        var createdTenant = new UserJoinToTenantPayload
        {
            TenantId = message.TenantId,
            UserId = message.UserId,
        };
        await _publisher.Publish(createdTenant);
        await _tenantService.CommitChangesAsync(context.CancellationToken);

    }
}
