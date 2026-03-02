using MassTransit;

namespace OnePunch.Notification.Core.Messaging;

public class UserInvitationWorker : IConsumer<UserInvitionNotificationPayload>
{
    private readonly EmailNotificationService _service;

    public UserInvitationWorker(EmailNotificationService service)
    {
        _service = service;
    }
    public async Task Consume(ConsumeContext<UserInvitionNotificationPayload> context)
    {
        var model = context.Message;
        var mailPayload = new Domain.DTO.MailPayload(model.Email, model.Token);
        var userInfo = new UserEmailPayload
        {
            TenantName = model.TenantName ?? model.AppName ?? "",
            ConfirmationRoute = model.InviteLink,
            Email = model.Email,
            FullName = model.Name ?? "User",
            AppName = model.AppName ?? "Erp system",
            Expiry = model.Expiry,
        };
        await _service.SendUserInvites(mailPayload, userInfo, context.CancellationToken);
    }
}