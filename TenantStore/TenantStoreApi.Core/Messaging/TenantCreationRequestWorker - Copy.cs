using MassTransit;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

public class PlanPaymentReceivedWorker : IConsumer<TenantPaymentReceived>
{
    private readonly SubscriptionService _subscriptionService;
    private readonly UnitOfWorkService _unitOfWorkService;
    private readonly PlanService _planService;
    private readonly IPublishEndpoint _publisher;

    public PlanPaymentReceivedWorker(
        SubscriptionService subscriptionService,
        UnitOfWorkService unitOfWorkService,
        PlanService planService,
        IPublishEndpoint publisher)
    {
        _subscriptionService = subscriptionService;
        _unitOfWorkService = unitOfWorkService;
        _planService = planService;
        _publisher = publisher;
    }

    public async Task Consume(ConsumeContext<TenantPaymentReceived> context)
    {

        var message = context.Message;
        var plan = await _planService.FindPlanAsync(message.PlanId);
        var sub = await _subscriptionService.FindOne(message.TenantId);
        if (sub == null) return;
        if (plan == null) return;

        sub.EndDate = sub.EndDate.AddDays(plan.Days);
        await _subscriptionService.UpdateAsync(sub);
        await _unitOfWorkService.CommitChangesAsync();

    }
}
