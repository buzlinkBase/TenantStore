using MassTransit;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Services;

namespace OnePunch.Auth.Core.Messaging;

/// <summary>
/// An account's subscription changed (SubscriptionChanged, published by Tenant Service). Pushes a
/// "SubscriptionChanged" signal to the live sessions of every member of the account's companies;
/// the frontend refetches the company's entitlements on it. Entitlements aren't in the token, so
/// nothing here has to be re-minted. Idempotent: a redelivery only repeats the signal.
/// </summary>
public class SubscriptionChangedWorker : IConsumer<SubscriptionChanged>
{
    private readonly TenantNotificationService _notificationService;

    public SubscriptionChangedWorker(TenantNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task Consume(ConsumeContext<SubscriptionChanged> context)
    {
        var message = context.Message;
        var recipients = message.MemberUserIds.Append(message.OwnerUserId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (recipients.Count == 0) return;

        await _notificationService.NotifySubscriptionChanged(recipients, new SubscriptionChangedNotification
        {
            TenantIds = message.TenantIds,
            Reason = message.Reason,
        });
    }
}
