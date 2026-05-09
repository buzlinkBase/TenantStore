using System.Reflection;

namespace OnePunch.Notification.Core.Extensions;

public static class LibServicesRegistrations
{
    public static void RegisterCoreServices(this WebApplicationBuilder builder)
    {
        AddLibraryAssemblyDependencies(builder.Services, "NotificationService");
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
