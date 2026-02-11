using Microsoft.Extensions.DependencyInjection;
using Onepunch.Common.Lib.Services;
using System.Reflection;

namespace OnePunch.Auth.Core;

public static class LibServicesRegistrations
{
    public static void RegisterCoreServices(this IServiceCollection services)
    {
        AddLibraryAssemblyDependencies(services, "Onepunch.Auth.Core");
        services.AddScoped<PasswordCrypto>();
        services.AddScoped<IUnitOfWorkService, UCommand>();
        services.AddSingleton(sp => sp.GetRequiredService<IServiceProvider>().GetRequiredService<IServiceScopeFactory>());
        services.AddScoped<IHMACService, HMACService>();
        services.AddScoped<IMessageHandlerFactory, MessageHandlerFactory>();
    }

    public static void AddLibraryAssemblyDependencies(IServiceCollection services, string assemblyName)
    {
        var libraryAssembly = Assembly.Load(assemblyName);
        var typesToRegister = libraryAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract
                && (type.Name.EndsWith("Service")
                    || type.Name.EndsWith("Provider")
                    || type.Name.EndsWith("Resolver")));

        foreach (var type in typesToRegister)
        {
            // Register the concrete type
            services.AddScoped(type);

            // Register interfaces implemented by this type
            var interfaces = type.GetInterfaces()
                .Where(i => i.Name == $"I{type.Name}"); // convention: I + class name

            foreach (var iface in interfaces)
            {
                services.AddScoped(iface, type);
            }
        }
    }
}
