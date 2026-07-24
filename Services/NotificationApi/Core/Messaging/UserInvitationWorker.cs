using MassTransit;

namespace OnePunch.Notification.Core.Messaging;

public class UserInvitationWorker : IConsumer<UserInvitionNotificationPayload>
{
    private readonly EmailNotificationService _emailService;

    public UserInvitationWorker(EmailNotificationService service)
    {
        _emailService = service;
    }
    public async Task Consume(ConsumeContext<UserInvitionNotificationPayload> context)
    {
        var model = context.Message;
        await _emailService.SendUserInvites(model, context.CancellationToken);
    }
}