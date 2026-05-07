using BuzlinkRepository;
using MassTransit;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Messaging;

public class TenantCreationRequestWorker : IConsumer<TenantCreationRequest>
{
    private readonly TenantService _tenantService;
    private readonly UserMembershipService _userMembershipService;
    private readonly ITenantProvider _tenantProvider;
    private readonly IPublishEndpoint _publisher;

    public TenantCreationRequestWorker(TenantService tenantService,
        UserMembershipService userMembershipService,
        ITenantProvider tenantProvider,
        IPublishEndpoint publisher)
    {
        _tenantService = tenantService;
        _userMembershipService = userMembershipService;
        _tenantProvider = tenantProvider;
        _publisher = publisher;
    }

    public async Task Consume(ConsumeContext<TenantCreationRequest> context)
    {

        var message = context.Message;
        var tenant = await _tenantService.CreateTenant(message, context.CancellationToken);
        _tenantProvider.SetTenantId(tenant.Id);
        await CreateMemberShip(tenant, context.CancellationToken);
        await PublishTenantAsync(tenant);
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }

    

    private async Task PublishTenantAsync(TenantModel tenant)
    {
        var createdTenant = new TenantCreatedPayload
        {
            Event = "tenant.created",
            TenantId = tenant.Id,
            UserId = tenant.UserId,
            TenantName = tenant.TenantName,
        };
        await _publisher.Publish(createdTenant);
    }

    private async Task CreateMemberShip(TenantModel tenant, CancellationToken token)
    {
        await _userMembershipService.AddAsync(new UserMembership
        {
            TenantId = tenant.Id,
            UserId = tenant.UserId,
            Role = "Owner"
        }, token);
    }
}
