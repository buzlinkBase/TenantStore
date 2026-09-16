using MassTransit;
namespace OnePunch.Notification.Core.Messaging;

public class ApprovalNotificationWorker : IConsumer<ApprovalNotificationRequested>
{
    private readonly EmailNotificationService _notificationService;
    public ApprovalNotificationWorker(EmailNotificationService notificationService)
    {
        _notificationService = notificationService;
    }
    public async Task Consume(ConsumeContext<ApprovalNotificationRequested> context)
    {
        var message = context.Message;
        await _notificationService.SendApprovalNotification(message, context.CancellationToken);
    }
}
