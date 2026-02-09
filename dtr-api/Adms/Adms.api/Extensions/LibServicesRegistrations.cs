using Adms.api.Services;
using BuzlinkRepository;
using System.Reflection;

namespace Adms.api.Extensions;

public static class LibServicesRegistrations
{
    public static void RegisterLibServices(this IServiceCollection services)
    {
        AddLibraryAssemblyDependencies(services, "Adms.api");
        services.AddScoped<ApplyTenantInterceptor>();
        services.AddScoped<IUnitOfWorkService, UCommand>();
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