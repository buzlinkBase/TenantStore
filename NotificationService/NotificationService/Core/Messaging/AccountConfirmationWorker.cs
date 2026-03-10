using MassTransit;
using OnePunch.Notification.Domain.DTO;

namespace OnePunch.Notification.Core.Messaging;

public class AccountConfirmationWorker : IConsumer<UserEmailPayload>
{
    private readonly EmailNotificationService _notificationService;
    public AccountConfirmationWorker(EmailNotificationService notificationService)
    {
        _notificationService = notificationService;
    }
    public async Task Consume(ConsumeContext<UserEmailPayload> context)
    {
        var message = context.Message;
        var payload = new MailPayload(message.Email, message.Token);
        await _notificationService.SendAccountConfirmation(payload, message.ConfirmationRoute, context.CancellationToken);
    }
}
