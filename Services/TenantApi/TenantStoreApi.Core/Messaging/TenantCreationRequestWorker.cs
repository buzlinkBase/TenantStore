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
    private readonly PlanService _planService;
    private readonly SubscriptionService _subscriptionService;

    public TenantCreationRequestWorker(
        TenantService tenantService,
        UserMembershipService userMembershipService,
        ITenantProvider tenantProvider,
        IPublishEndpoint publisher,
        PlanService planService,
        SubscriptionService subscriptionService)
    {
        _tenantService = tenantService;
        _userMembershipService = userMembershipService;
        _tenantProvider = tenantProvider;
        _publisher = publisher;
        _planService = planService;
        _subscriptionService = subscriptionService;
    }

    public async Task Consume(ConsumeContext<TenantCreationRequest> context)
    {
        var message = context.Message;
        var tenant = await _tenantService.CreateTenant(message, context.CancellationToken);
        _tenantProvider.SetTenantId(tenant.Id);
        await CreateMembershipAsync(tenant, context.CancellationToken);
        await CreateFreeTrialAsync(tenant, context.CancellationToken);
        await _publisher.Publish(new TenantCreatedPayload
        {
            Event = "tenant.created",
            TenantId = tenant.Id,
            UserId = tenant.UserId,
            TenantName = tenant.TenantName,
        });
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }

    private async Task CreateMembershipAsync(TenantModel tenant, CancellationToken token)
    {
        await _userMembershipService.AddAsync(new UserMembership
        {
            TenantId = tenant.Id,
            UserId = tenant.UserId,
            Role = "Owner"
        }, token);
    }

    private async Task CreateFreeTrialAsync(TenantModel tenant, CancellationToken token)
    {
        var freeTrial = await _planService.FindFreeTrialAsync(token);
        if (freeTrial == null) return;

        await _subscriptionService.AddAsync(new TenantSubscription
        {
            TenantId = tenant.Id,
            PlanId = freeTrial.Id,
            SubStatus = SubscriptionStatus.Trialing,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(freeTrial.Days > 0 ? freeTrial.Days : 14),
        }, token);
    }
}
