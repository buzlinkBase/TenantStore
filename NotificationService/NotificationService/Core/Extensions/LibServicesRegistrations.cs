using Onepunch.Common.Lib.Services;
using System.Reflection;

namespace OnePunch.Notification.Core.Extensions;

public static class LibServicesRegistrations
{
    public static void RegisterCoreServices(this IServiceCollection services)
    {
        AddLibraryAssemblyDependencies(services, "NotificationService");
        services.AddScoped<PasswordCrypto>();
        services.AddScoped<IUnitOfWorkService, UCommand>();
        services.AddSingleton(sp => sp.GetRequiredService<IServiceProvider>().GetRequiredService<IServiceScopeFactory>());
        services.AddScoped<IHMACService, HMACService>();
        services.AddScoped<IMessageHandlerFactory, MessageHandlerFactory>();
        services.AddScoped<IRabbitMQPublisher, RabbitMQPublisher>();
        services.AddHostedService<RabbitMQConsumer>();
    }

    public static void AddLibraryAssemblyDependencies(IServiceCollection services, string assemblyName)
    {
        var libraryAssembly = Assembly.Load(assemblyName);
        var typesToRegister = libraryAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.Name.EndsWith("Service"));
        foreach (var type in typesToRegister)
        {
            services.AddScoped(type);
        }
    }
}
