
using Newtonsoft.Json;

namespace OnePunch.Notification.Core.Messaging;

public class UserInviteNotificationHandler : IMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    public UserInviteNotificationHandler(IServiceScopeFactory scopeFactory, IRabbitMQPublisher publisher)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task Handle(string message)
    {
        var model = ObjectSerializer.DeSerialized<MessagePayload<UserEmailPayload>>(message);
        if (model == null)
        {
            Log.Logger.Error("Unable to deserialize tenant confirmation email payload");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
        var notifService = scope.ServiceProvider.GetRequiredService<EmailNotificationService>();

        try
        {
            await SendConfirmationEmailAsync(notifService, model.Data);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to send user invites");
            throw;
        }
    }

    private static async Task SendConfirmationEmailAsync(EmailNotificationService notifService, UserEmailPayload model)
    {
        var mailPayload = new Domain.DTO.MailPayload(model.Email, model.Token);
        notifService.SendUserInvites(mailPayload,  model);
        await Task.CompletedTask;
    }
}