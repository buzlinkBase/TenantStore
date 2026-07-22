using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
namespace Onepunch.Common.Lib;

public static class MapsterConfiguration
{
    public static void AddMapster(this IServiceCollection services, params Assembly[] assembliesToScan)
    {
        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(assembliesToScan);
        services.AddSingleton(config);
        services.AddScoped<IMapper, ServiceMapper>();
    }
}
