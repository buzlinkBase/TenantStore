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
                Role = existing.Role,
            });
            return;
        }

        var role = string.IsNullOrWhiteSpace(message.Role) ? "Member" : message.Role;
        await _userMembershipService.AddAsync(new UserMembership
        {
            TenantId = message.TenantId,
            UserId = message.UserId,
            Role = role,
        }, context.CancellationToken);

        await _publisher.Publish(new UserJoinToTenantPayload
        {
            TenantId = message.TenantId,
            TenantName = message.TenantName,
            UserId = message.UserId,
            Role = role,
        });
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }
}
