using BuzlinkRepository;
using MassTransit;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

public class UserCreatedWorker : IConsumer<UserCreated>
{
    private readonly TenantService _tenantService;
    private readonly UserMembershipService _userMembershipService;
    private readonly SubscriptionService _subscriptionService;
    private readonly ITenantProvider _tenantProvider;
    private readonly ConnectionService _connectionService;
    private readonly PlanService _planService;
    private readonly IPublishEndpoint _publisher;

    public UserCreatedWorker(TenantService tenantService,
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

    public async Task Consume(ConsumeContext<UserCreated> context)
    {
        var message = context.Message;
        var tenant = await _tenantService
            .CreateTenant(message, context.CancellationToken);
        _tenantProvider.SetTenantId(tenant.Id);
        await CreateMemberShip(tenant, context.CancellationToken);
        await CreateSubsAsync(context, tenant);
        await PublishTenantAsync(tenant);
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }

    public async Task CreateSubsAsync(ConsumeContext<UserCreated> context, TenantModel tenant)
    {
        var freePlan = await _planService.FindFreeTrialAsync(context.CancellationToken);
        if (freePlan == null)
        {
            freePlan = new Plan
            {
                Description = "Free Trial",
                Name = "Free Trial"
            };
            _planService.Repository.Add(freePlan);
            await _planService.SaveChangesAsync(context.CancellationToken);
        }
        var sub = new TenantSubscription
        {
            TenantId = tenant.Id,
            PlanId = freePlan.Id,
            SubStatus = SubscriptionStatus.Trialing,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(30)
        };
        await _subscriptionService.AddAsync(sub, context.CancellationToken);
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
