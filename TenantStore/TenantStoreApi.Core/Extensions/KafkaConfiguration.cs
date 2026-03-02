using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Extensions;

public static class KafkaConfiguration
{
    public static void ConfigKafka(this WebApplicationBuilder builder)
    {

        var settings = builder.Configuration.GetSection(nameof(KafkaSettings)).Get<KafkaSettings>();
        if (settings == null) return;

        builder.Services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<TenantContext>(o =>
            {
                o.UseMySql();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(5);
                o.UseBusOutbox();
                o.DisableInboxCleanupService();
            });

            x.UsingInMemory((context, cfg) => { cfg.ConfigureEndpoints(context); });
            x.AddRider(rider =>
            {
                rider.AddConsumer<UserConfirmedWorker>();
                rider.AddProducer<TenantCreatedPayload>(settings.Topics.TenantCreated);
                rider.UsingKafka((context, k) =>
                {
                    k.Host(settings.BootstrapServers);
                    k.TopicEndpoint<TenantUserPayload>(settings.Topics.TenantUserConfirmed, "tenantstore-service:admin-user-confirmed-group", e =>
                    {
                        e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                        e.ConfigureConsumer<UserConfirmedWorker>(context);
                    });

                });
            });
        });
    }
}
