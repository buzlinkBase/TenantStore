using MassTransit;
namespace OnePunch.Notification.Core.Messaging;

public class SubscriptionReminderWorker : IConsumer<SubscriptionReminderEmail>
{
    private readonly EmailNotificationService _notificationService;
    public SubscriptionReminderWorker(EmailNotificationService notificationService)
    {
        _notificationService = notificationService;
    }
    public async Task Consume(ConsumeContext<SubscriptionReminderEmail> context)
    {
        await _notificationService.SendSubscriptionReminder(context.Message, context.CancellationToken);
    }
}
