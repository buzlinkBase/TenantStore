using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Onepunch.Auth.Core;
using OnePunch.Auth.Core.Messaging;

namespace OnePunch.Auth.Core;

public static class KafkaConfiguration
{
    public static void ConfigKafka(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection(nameof(KafkaSettings)).Get<KafkaSettings>();
        if (settings == null) return;

        builder.Services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<AuthContext>(o =>
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
                rider.AddProducer<TenantUserPayload>(settings.Topics.TenantUserConfirmed);
                rider.AddProducer<UserEmailPayload>(settings.Topics.UserCreated);
                rider.AddProducer<UserInvitionNotificationPayload>(settings.Topics.SendUserInvitation);
                rider.AddConsumer<TenantCreatedWorker>();
                rider.UsingKafka((context, k) =>
                {
                    k.Host(settings.BootstrapServers);
                    k.TopicEndpoint<TenantCreatedPayload>(settings.Topics.TenantCreated, "auth-service:consume-tenant-created-group", e =>
                    {
                        e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                        e.ConfigureConsumer<TenantCreatedWorker>(context);
                    });
                });
            });
        });
    }
}