using MassTransit;
using TenantStoreApi.Core.Services;
namespace TenantStoreApi.Core.Messaging;

//TODO not implemented yet in auth service
public class TenantJoinWorker : IConsumer<TenantJoin>
{
    private readonly TenantService _tenantService;
    private readonly IPublishEndpoint _publisher;

    public TenantJoinWorker(TenantService tenantService,
        IPublishEndpoint publisher)
    {
        _tenantService = tenantService;
        _publisher = publisher;
    }

    public async Task Consume(ConsumeContext<TenantJoin> context)
    {
        var message = context.Message;

        //no tenant just a member
        var model = new TenantDelegation
        {
            GuestTenantId = message.GuestTenantId,
            HostTenantId = message.HostTenantId,
            GuestTenantName = "",
            AccessLevel = ""
        };
        _tenantService.Repository.Add(model);

         //publish tenant join
        //await _publisher.Publish(createdTenant);
        await _tenantService.CommitChangesAsync(context.CancellationToken);

    }
}
