using MassTransit;

using OnePunch.Notification.Core.Messaging;
using OnePunch.Notification.Infrastructure;

namespace OnePunch.Notification.Core.Extensions;
//public static class NotifTenantKafkaConfiguration
//{
//    public static void NotifConfigKafka(this WebApplicationBuilder builder)
//    {

//        var settings = builder.Configuration.GetSection(nameof(KafkaSettings)).Get<KafkaSettings>();
//        if (settings == null) return;

//        builder.Services.AddMassTransit(x =>
//        {
//            x.AddEntityFrameworkOutbox<NotifContext>(o =>
//            {
//                o.UseMySql();
//                o.UseBusOutbox();
//                o.QueryDelay = TimeSpan.FromSeconds(5);
//                o.UseBusOutbox();
//                o.DisableInboxCleanupService();
//            });

//            x.UsingInMemory((context, cfg) => { cfg.ConfigureEndpoints(context); });
//            x.AddRider(rider =>
//            {
//                rider.AddConsumer<UserInvitationWorker>();
//                rider.AddConsumer<UserCreatedWorker>();
//                rider.AddProducer<TenantCreatedPayload>(settings.Topics.TenantCreated);

//                rider.UsingKafka((context, k) =>
//                {
//                    k.Host(settings.BootstrapServers);
//                    k.TopicEndpoint<UserEmailPayload>(settings.Topics.UserCreated, "notification-service:consume-user-created-group", e =>
//                    {
//                        e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
//                        e.ConfigureConsumer<UserCreatedWorker>(context);
//                    });

//                    k.TopicEndpoint<UserInvitionNotificationPayload>(settings.Topics.SendUserInvitation, "notification-service:consume-user-invited-group", e =>
//                    {
//                        e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
//                        e.ConfigureConsumer<UserInvitationWorker>(context);
//                    });
//                });
//            });
//        });
//    }
//}

public static class NotifTenantRabbitConfiguration
{
    public static void NotifConfigRabbitMq(this WebApplicationBuilder builder)
    {
        // Ensure you have a RabbitMqSettings class in your appsettings.json
        var settings = builder.Configuration.GetSection(nameof(RabbitMqSettings)).Get<RabbitMqSettings>();
        if (settings == null) return;

        builder.Services.AddMassTransit(x =>
        {
            // 1. Register Consumers globally (removed from inside a 'Rider')
            x.AddConsumer<UserInvitationWorker>();
            x.AddConsumer<UserCreatedWorker>();
            x.AddConsumer<ResetPasswordWorker>();
            // 2. Keep the Outbox configuration (it is transport-agnostic)
            x.AddEntityFrameworkOutbox<NotifContext>(o =>
            {
                o.UseMySql();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(5);
                o.DisableInboxCleanupService();
            });

            // 3. Configure RabbitMQ
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(settings.Host, settings.VirtualHost, h =>
                {
                    h.Username(settings.Username);
                    h.Password(settings.Password);
                });

                // This automatically maps consumers to queues based on their name
                cfg.ConfigureEndpoints(context);
            });
        });
    }
}
