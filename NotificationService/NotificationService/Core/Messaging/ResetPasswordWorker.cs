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
        var mailPayload = new Domain.DTO.MailPayload(message.Email, message.Token);
        await _service.SendResetPassword(mailPayload, message, context.CancellationToken);
    }
}