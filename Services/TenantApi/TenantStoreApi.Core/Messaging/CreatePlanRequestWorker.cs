using BuzlinkRepository;
using MassTransit;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Messaging;

public class CreatePlanRequestWorker : IConsumer<PlanRequest>
{
    private readonly TenantService _tenantService;
    private readonly SubscriptionService _subscriptionService;
    private readonly ITenantProvider _tenantProvider;
    private readonly PlanService _planService;
    private readonly IPublishEndpoint _publisher;

    public CreatePlanRequestWorker(
        TenantService tenantService,
        UserMembershipService userMembershipService,
        SubscriptionService subscriptionService,
        ITenantProvider tenantProvider,
        PlanService planService,
        IPublishEndpoint publisher)
    {
        _tenantService = tenantService;
        _subscriptionService = subscriptionService;
        _tenantProvider = tenantProvider;
        _planService = planService;
        _publisher = publisher;
    }

    public async Task Consume(ConsumeContext<PlanRequest> context)
    {
        var message = context.Message;
        _tenantProvider.SetTenantId(message.TenantId);
        await CreateSubsAsync(message, context.CancellationToken);
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }

    private async Task CreateSubsAsync(PlanRequest model, CancellationToken token)
    {
        var plan = await _planService.FindPlanAsync(model.PlanId, token);
        if (plan == null)
        {
            await _publisher.Publish(new ServicePlanNotFound
            {
                PlanId = model.PlanId,
                TenantId = model.TenantId,
                UserId = model.UserId,
            });
            return;
        }

        var existing = await _subscriptionService.FindActiveAsync(model.TenantId, token);
        if (existing != null)
        {
            existing.SubStatus = SubscriptionStatus.Expired;
            await _subscriptionService.UpdateAsync(existing, token);
        }

        var endDate = model.ValidUntil ?? DateTime.UtcNow.AddDays(plan.Days);
        await _subscriptionService.AddAsync(new TenantSubscription
        {
            TenantId = model.TenantId,
            PlanId = model.PlanId,
            SubStatus = endDate > DateTime.UtcNow ? SubscriptionStatus.Active : SubscriptionStatus.Expired,
            StartDate = DateTime.UtcNow,
            EndDate = endDate,
        }, token);
    }
}
