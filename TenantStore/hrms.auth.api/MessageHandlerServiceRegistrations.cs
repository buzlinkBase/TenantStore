

using Onepunch.Common.Lib;
using OnePunch.Auth.Core.Messaging;

namespace OnePunch.Auth.Api;

public static class MessageHandlerServiceRegistrations
{

    public static void RegisterMessageHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddKeyedScoped<IMessageHandler, TenantCreatedHandler>("tenant.created");
        builder.Services.AddKeyedScoped<IMessageHandler, TenantForConfirmationHandler>("tenant.for.confirmation");
        builder.Services.AddKeyedScoped<IMessageHandler, TenantEmailNotifSuccessHandler>("tenant.confirmation.email.sent");
        builder.Services.AddKeyedScoped<IMessageHandler, TenantActivatedHandler>("tenant.activated");
    }
}