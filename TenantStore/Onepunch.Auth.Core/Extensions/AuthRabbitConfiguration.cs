using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Onepunch.Auth.Core;
using Onepunch.Auth.Infrastructure;
using OnePunch.Auth.Core.Messaging;

namespace OnePunch.Auth.Core; 
public static class AuthRabbitConfiguration
{
    public static void AuthConfigRabbitMq(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection(nameof(RabbitMqSettings)).Get<RabbitMqSettings>();
        if (settings == null) return;

        builder.Services.AddMassTransit(x =>
        {
            // 1. Register Consumer
            x.AddConsumer<TenantCreatedWorker>(); 
            // 2. Outbox configuration
            x.AddEntityFrameworkOutbox<AuthContext>(o =>
            {
                o.UseMySql();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(5);
                o.DisableInboxCleanupService();
            });

            // 3. Configure RabbitMQ
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.UseMessageRetry(r =>
                {
                    r.Exponential(5, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));
                });

                // Circuit Breaker prevents slamming a failing service
                cfg.UseCircuitBreaker(cb =>
                {
                    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                    cb.TripThreshold = 15; // Trip after 15 failures
                    cb.ResetInterval = TimeSpan.FromMinutes(5); // Wait 5 mins before trying again
                });

                cfg.UsePublishFilter(typeof(TenantPublishFilter<>), context);
                cfg.UseConsumeFilter(typeof(TenantConsumeFilter<>), context);
                cfg.ConfigureEndpoints(context);
                cfg.Host(settings.Host, settings.VirtualHost, h =>
                {
                    h.Username(settings.Username);
                    h.Password(settings.Password);
                });
                 
            });
        });
    }
}
