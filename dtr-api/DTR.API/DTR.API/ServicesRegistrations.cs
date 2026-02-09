using DTR.Infrastructure;
using System.Data;
using System.Reflection;

namespace DTR.Api;

public static class ServicesRegistration
{
    public static void DTRServiceConfig(this IServiceCollection services)
    {
        services.AddScoped<IDTRUnitOfWork, UCommand>();
        services.AddSingleton<ITenantProvider, TenantProvider>();
        services.AddScoped<IAppConfigurationProvider, WebAppConfigurationProvider>();
        services.AddScoped<ITenantContextAccessor, WebTenantContextAccessor>();
        services.AddScoped<IDbConnectionProvider, DbConnectionProvider>();
        services.AddAutoMapper(typeof(AutoMapperProfile));
        services.AddScoped<DataServiceResolver>();
        services.AddScoped<IUnitOfWork<DTRDbContext>>(provider => provider.GetRequiredService<IDTRUnitOfWork>());
        AddLibraryAssemblyDependencies(services, "DTR.Core");
    }
    public static void AddLibraryAssemblyDependencies(IServiceCollection services, string assemblyName)
    {
        var libraryAssembly = Assembly.Load(assemblyName);
        var typesToRegister = libraryAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract
            && type.Name.EndsWith("Service"));
        foreach (var type in typesToRegister)
        {
            services.AddScoped(type);
        }
    }
}