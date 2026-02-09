using OnePunch.Notification.Core.Messaging;
namespace OnePunch.Notification;

public static class MessageHandlerServiceRegistrations
{
    public static void RegisterMessageHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddKeyedScoped<IMessageHandler, SendTenantConfirmationEmailHandler>("notif.tenant.for.confirmation");
        builder.Services.AddKeyedScoped<IMessageHandler, UserInviteNotificationHandler>("notif.send.user.invitation");
        builder.Services.AddKeyedScoped<IMessageHandler, SuccessNotifHandler>("notif.success");
    }
}