using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace TenantStoreApi.Core.Extensions;

public static class LibServicesRegistrations
{
    public static void RegisterCoreServices(this WebApplicationBuilder builder)
    {
        AddLibraryAssemblyDependencies(builder.Services, "TenantStoreApi.Core"); 
        builder.Services.AddScoped<PasswordCrypto>();
        builder.Services.AddScoped<IUnitOfWorkService, UnitOfWorkService>();
        builder.Services.AddScoped<IHMACService, HMACService>();
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
