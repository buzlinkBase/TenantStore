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
        // 1. Only fetch what you actually need
        var settings = builder.Configuration.GetSection("RabbitMqSettings").Get<RabbitMqSettings>();
        if (settings == null) return;

        builder.Services.AddMassTransit(x =>
        {
            // 2. Register Consumers
            x.AddConsumer<UserConfirmedWorker>();
            x.AddConsumer<TenantUserCreatedWorker>();
            // 3. EF Core Outbox
            x.AddEntityFrameworkOutbox<TenantContext>(o =>
            {
                o.UseMySql();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(5);
                o.DisableInboxCleanupService();
            });

            // 4. Configure RabbitMQ Transport
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
                cfg.Host(settings.Host, settings.VirtualHost, h =>
                {
                    h.Username(settings.Username);
                    h.Password(settings.Password);
                });
                // Note: You do not need AddProducer for RabbitMQ.
                // Just inject IPublishEndpoint into your services and publish directly.
                // Map your specific endpoints

                //cfg.ReceiveEndpoint("tenantstore-service-user-confirmed", e =>
                //{
                //    e.ConfigureConsumer<UserConfirmedWorker>(context);
                //    e.Durable = true;
                //    e.AutoDelete = false; // Never auto-delete your durable queues
                //});

                //cfg.ReceiveEndpoint("tenantstore-service-user-created", e =>
                //{
                //    e.ConfigureConsumer<TenantUserCreatedWorker>(context);
                //    e.Durable = true;
                //    e.AutoDelete = false; // Never auto-delete your durable queues
                //});
                // This handles any consumers not explicitly mapped above
                cfg.ConfigureEndpoints(context);
            });
        });
    }
}
