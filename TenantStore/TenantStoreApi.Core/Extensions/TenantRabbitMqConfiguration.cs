using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using TenantStoreApi.Core.Messaging;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Extensions;

public static class TenantRabbitMqConfiguration
{
    public static void TenantConfigRabbitMq(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection("RabbitMqSettings").Get<RabbitMqSettings>();
        if (settings == null) return;

        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<UserCreatedWorker, UserCreatedDefinition>();
            x.SetEndpointNameFormatter(KebabCaseEndpointNameFormatter.Instance);
            x.AddEntityFrameworkOutbox<TenantContext>(o =>
            {
                o.UseMySql();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(5);
                o.DisableInboxCleanupService();
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.UseMessageRetry(r =>
                {
                    r.Exponential(5, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));
                });
                cfg.UseCircuitBreaker(cb =>
                {
                    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                    cb.TripThreshold = 15;
                    cb.ResetInterval = TimeSpan.FromMinutes(5);
                });
                cfg.Host(settings.Host, settings.VirtualHost, h =>
                {
                    h.Username(settings.Username);
                    h.Password(settings.Password);
                });
                cfg.ConfigureEndpoints(context);
            });
        });
    }
}

public class UserCreatedDefinition : ConsumerDefinition<UserCreatedWorker>
{
    public UserCreatedDefinition()
    {
        EndpointName = "tenant-user-confirmation-que";
    } 
}
