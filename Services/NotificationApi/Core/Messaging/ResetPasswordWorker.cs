using MassTransit;


namespace OnePunch.Notification.Core.Messaging;

public class ResetPasswordWorker : IConsumer<ResetPasswordEmail>
{
    private readonly EmailNotificationService _service;

    public ResetPasswordWorker(EmailNotificationService service)
    {
        _service = service;
    }
    public async Task Consume(ConsumeContext<ResetPasswordEmail> context)
    {
        var message  = context.Message;
        await _service.SendResetPassword(message, context.CancellationToken);
    }
}