using MassTransit;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;
public class UserCreatedWorker : IConsumer<UserCreated>
{
    private readonly TenantService _tenantService;
    private readonly IPublishEndpoint _publisher;
    public UserCreatedWorker(TenantService tenantService,
        IPublishEndpoint publisher)
    {
        _tenantService = tenantService;
        _publisher = publisher;
    }
    public async Task Consume(ConsumeContext<UserCreated> context)
    {
        var message = context.Message;
        var result = await _tenantService.CreateTenant(message, context.CancellationToken);
        var createdTenant = new TenantCreatedPayload
        {
            TenantId = result.Id,
            UserId = message.UserId,
            CompanyName = result.CompanyName,
        };
        await _publisher.Publish(createdTenant);
        await _tenantService.CommitChangesAsync(context.CancellationToken);

    }
}
