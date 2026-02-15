using BuzlinkRepository;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.Common.Lib.Services;
using System.Reflection;

namespace TenantStoreApi.Core.Extensions;
public static class LibServicesRegistrations
{
    public static void RegisterCoreServices(this WebApplicationBuilder builder)
    {
        AddLibraryAssemblyDependencies(builder.Services, "TenantStoreApi.Core"); 
        builder.Services.AddScoped<PasswordCrypto>();
        builder.Services.AddScoped<IUnitOfWorkService, UCommand>();
        builder.Services.AddScoped<IHMACService, HMACService>();
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