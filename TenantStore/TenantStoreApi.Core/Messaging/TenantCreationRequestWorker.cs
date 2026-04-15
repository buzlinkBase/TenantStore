using BuzlinkRepository;
using MassTransit;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Messaging;

public class TenantCreationRequestWorker : IConsumer<TenantCreationRequest>
{
    private readonly TenantService _tenantService;
    private readonly UserMembershipService _userMembershipService;
    private readonly SubscriptionService _subscriptionService;
    private readonly ITenantProvider _tenantProvider;
    private readonly PlanService _planService;
    private readonly IPublishEndpoint _publisher;

    public TenantCreationRequestWorker(TenantService tenantService,
        UserMembershipService userMembershipService,
        SubscriptionService subscriptionService,
        ITenantProvider tenantProvider,
        PlanService planService,
        IPublishEndpoint publisher)
    {
        _tenantService = tenantService;
        _userMembershipService = userMembershipService;
        _subscriptionService = subscriptionService;
        _tenantProvider = tenantProvider;
        _planService = planService;
        _publisher = publisher;
    }

    public async Task Consume(ConsumeContext<TenantCreationRequest> context)
    {

        var message = context.Message;
        var tenant = await _tenantService.CreateTenant(message, context.CancellationToken);
        _tenantProvider.SetTenantId(tenant.Id);
        await CreateMemberShip(tenant, context.CancellationToken);
        await CreateSubsAsync(message, tenant, context.CancellationToken);
        await PublishTenantAsync(tenant);
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }

    public async Task CreateSubsAsync(TenantCreationRequest model,
        TenantModel tenant, CancellationToken token)
    {
        var plan = await _planService.FindPlanAsync(tenant.Id, token);
        if (plan == null)
        {
            var notFound = new ServicePlanNotFound
            {
                PlanId = model.Plan.PlanId,
                TenantId = tenant.Id,
                UserId = model.UserId,
            };
            await _publisher.Publish(notFound);
            return;
        }
        var sub = new TenantSubscription
        {
            TenantId = tenant.Id,
            PlanId = model.Plan.PlanId,
            SubStatus = SubscriptionStatus.Active,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow,
            //DateTime.UtcNow.AddDays(plan.Days) //this can be incremented when payment received/paid
        };
        await _subscriptionService.AddAsync(sub);
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
