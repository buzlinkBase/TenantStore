using MassTransit;

namespace OnePunch.Notification.Core.Messaging;

/// <summary>Delivers a message composed by another service (see <see cref="SendEmailRequested"/>).</summary>
public class SendEmailWorker : IConsumer<SendEmailRequested>
{
    private readonly EmailNotificationService _notificationService;

    public SendEmailWorker(EmailNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task Consume(ConsumeContext<SendEmailRequested> context)
    {
        await _notificationService.SendGeneralEmail(context.Message, context.CancellationToken);
    }
}
