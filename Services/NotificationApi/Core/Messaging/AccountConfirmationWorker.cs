using MassTransit;
namespace OnePunch.Notification.Core.Messaging;
public class AccountConfirmationWorker : IConsumer<SendAccountVerification>
{
    private readonly EmailNotificationService _notificationService;
    public AccountConfirmationWorker(EmailNotificationService notificationService)
    {
        _notificationService = notificationService;
    }
    public async Task Consume(ConsumeContext<SendAccountVerification> context)
    {
        var message = context.Message;
        await _notificationService.SendAccountConfirmation(message, context.CancellationToken);
    }
}
