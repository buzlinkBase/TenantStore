using BuzlinkRepository;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Extensions;

//public static class TenantKafkaConfiguration
//{
//    public static void TenantConfigKafka(this WebApplicationBuilder builder)
//    {

//        var settings = builder.Configuration.GetSection(nameof(KafkaSettings)).Get<KafkaSettings>();
//        if (settings == null) return;

//        builder.Services.AddMassTransit(x =>
//        {
//            x.AddEntityFrameworkOutbox<TenantContext>(o =>
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
//                rider.AddConsumer<UserConfirmedWorker>();
//                rider.AddConsumer<TenantUserCreatedWorker>();
//                rider.AddProducer<TenantCreatedPayload>(settings.Topics.TenantCreated);

//                rider.UsingKafka((context, k) =>
//                {
//                    k.Host(settings.BootstrapServers);
//                    k.TopicEndpoint<TenantUserPayload>(settings.Topics.TenantUserConfirmed, "tenantstore-service:admin-user-confirmed-group", e =>
//                    {
//                        e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
//                        e.ConfigureConsumer<UserConfirmedWorker>(context);
//                    });
//                    k.TopicEndpoint<UserEmailPayload>(settings.Topics.TenantUserConfirmed, "tenantstore-service:admin-user-created-group", e =>
//                    {
//                        e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
//                        e.ConfigureConsumer<TenantUserCreatedWorker>(context);
//                    });

//                });
//            });
//        });
//    }
//}

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
                cfg.ReceiveEndpoint("tenantstore-service-user-confirmed", e =>
                {
                    e.ConfigureConsumer<UserConfirmedWorker>(context);
                });

                cfg.ReceiveEndpoint("tenantstore-service-user-created", e =>
                {
                    e.ConfigureConsumer<TenantUserCreatedWorker>(context);
                });

                // This handles any consumers not explicitly mapped above
                cfg.ConfigureEndpoints(context);
            });
        });
    }
}

public class TenantPublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    private readonly ITenantProvider _tenantProvider;
    public TenantPublishFilter(ITenantProvider tenantProvider) => _tenantProvider = tenantProvider;
    public void Probe(ProbeContext context) => context.CreateFilterScope("tenant-publish-filter");
    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        // Inject the TenantId into the message headers
        context.Headers.Set("X-Tenant-ID", _tenantProvider.TenantId.ToString());
        await next.Send(context);
    }
}

public class TenantConsumeFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private readonly ITenantProvider _tenantProvider;
    public TenantConsumeFilter(ITenantProvider tenantProvider) => _tenantProvider = tenantProvider;

    public void Probe(ProbeContext context) => context.CreateFilterScope("tenant-consume-filter");

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        // Extract the header and set it in your provider
        if (context.Headers.TryGetHeader("X-Tenant-ID", out var value) &&
            Guid.TryParse(value?.ToString(), out var tenantId))
        {
            _tenantProvider.SetTenantId(tenantId);
        }
        await next.Send(context);
    }
}