using Microsoft.Extensions.DependencyInjection;
using Onepunch.Auth.Core.Interfaces;
using System.Reflection;

namespace OnePunch.Auth.Core;

public static class LibServicesRegistrations
{
    public static void RegisterCoreServices(this IServiceCollection services)
    {
        AddLibraryAssemblyDependencies(services, "Onepunch.Auth.Core");
        services.AddScoped<PasswordCrypto>();
        services.AddScoped<IUnitOfWorkService, UnitOfWorkService>();
        services.AddScoped<IHMACService, HMACService>();
        services.AddScoped<MembershipGrpcClient>();
        services.AddScoped<IMfaChallengeService, NoOpMfaChallengeService>();
    }

    public static void AddLibraryAssemblyDependencies(IServiceCollection services, string assemblyName)
    {
        var libraryAssembly = Assembly.Load(assemblyName);
        var typesToRegister = libraryAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.Name.EndsWith("Service"));

        foreach (var type in typesToRegister)
        {
            var attr = type.GetCustomAttribute<ServiceRegistrationAttribute>();
            if (attr != null && attr.Exclude) continue;
            var lifetime = attr?.Lifetime ?? ServiceLifetime.Scoped;
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    services.AddSingleton(type);
                    break;
                case ServiceLifetime.Transient:
                    services.AddTransient(type);
                    break;
                default:
                    services.AddScoped(type);
                    break;
            }
        }
    }
}
