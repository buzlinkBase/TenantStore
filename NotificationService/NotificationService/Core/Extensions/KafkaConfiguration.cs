using MassTransit;
using OnePunch.Notification.Core.Messaging;

namespace OnePunch.Notification.Core.Extensions;

public static class KafkaConfiguration
{
    public static void ConfigKafka(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection(nameof(KafkaSettings)).Get<KafkaSettings>();
        if (settings == null) return;

        builder.Services.AddMassTransit(x =>
        {
            x.UsingInMemory((context, cfg) => { cfg.ConfigureEndpoints(context); });
            x.AddRider(rider =>
            {
                rider.AddConsumers(typeof(KafkaConfiguration).Assembly);
                rider.UsingKafka((context, k) =>
                {
                    k.Host(settings.BootstrapServers);
                    k.TopicEndpoint<UserEmailPayload>(settings.Topics.UserCreated, "notification-service:consume-user-created-group", e =>
                    {
                        e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                        e.ConfigureConsumer<UserCreatedWorker>(context);
                    });

                    k.TopicEndpoint<UserInvitionNotificationPayload>(settings.Topics.SendUserInvitation, "notification-service:consume-user-invited-group", e =>
                    {
                        e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                        e.ConfigureConsumer<UserInvitationWorker>(context);
                    });
                });
            });
        });
    }
}