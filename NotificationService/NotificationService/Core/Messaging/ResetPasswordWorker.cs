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
        var model = context.Message;
        var mailPayload = new Domain.DTO.MailPayload(model.Email, model.Token);
        var userInfo = new ResetPasswordEmail
        {
            TenantName = model.TenantName ?? model.AppName ?? "",
            ResetLink = model.ResetLink,
            Email = model.Email,
            Name = model.Name ?? "User",
            AppName = model.AppName ?? "Erp system",
            Expiry = model.Expiry,
        };
        await _service.SendResetPassword(mailPayload, userInfo, context.CancellationToken);
    }
}