
using Onepunch.Common.Lib;

namespace TenantStoreApi;

public static class MessageHandlerServiceRegistrations
{

    public static void RegisterMessageHandlers(this WebApplicationBuilder builder)
    {

        builder.Services.AddKeyedScoped<IMessageHandler, AdminUserCreatedHandler>("admin.user.created");
        builder.Services.AddKeyedScoped<IMessageHandler, TenantEmailConfirmedHandler>("admin.user.email.confirmed");

    }
}