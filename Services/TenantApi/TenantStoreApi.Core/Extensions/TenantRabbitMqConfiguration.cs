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
            x.AddConsumer<TenantCreationRequestWorker, UserCreatedDefinition>();
            x.AddConsumer<UserJoinWorker, UserJoinDefinition>();
            x.AddConsumer<TenantJoinWorker, TenantJoinDefinition>();
            x.AddConsumer<DbCreatedWorker, DbCreatedWorkerDefinition>();
            x.AddConsumer<SchemaMigrationUpdatedWorker, SchemaMigrationUpdatedWorkerDefinition>();
            x.AddConsumer<UserInvitedWorker, UserInvitedWorkerDefinition>();
            x.AddConsumer<InvitationAcceptedWorker, InvitationAcceptedWorkerDefinition>();
            x.SetEndpointNameFormatter(KebabCaseEndpointNameFormatter.Instance);
            x.AddEntityFrameworkOutbox<TenantContext>(o =>
            {
                o.UseMySql();
                o.UseBusOutbox();
                //o.QueryDelay = TimeSpan.FromSeconds(5);
                //o.DisableInboxCleanupService();
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
                //cfg.Host(settings.Host, settings.VirtualHost, h =>
                //{
                //    h.Username(settings.Username);
                //    h.Password(settings.Password);
                //});
                cfg.Host(settings.Uri);
                cfg.UsePublishFilter(typeof(TenantPublishFilter<>), context);
                cfg.SetQuorumQueue();
                cfg.ConfigureEndpoints(context);
            });
        });
    }
}

public class UserCreatedDefinition : ConsumerDefinition<TenantCreationRequestWorker>
{
    public UserCreatedDefinition()
    {
        EndpointName = "tenant-service-create-user-que";
    }
}

public class UserJoinDefinition : ConsumerDefinition<UserJoinWorker>
{
    public UserJoinDefinition()
    {
        EndpointName = "tenant-service-user-join-que";
    }
}
public class TenantJoinDefinition : ConsumerDefinition<TenantJoinWorker>
{
    public TenantJoinDefinition()
    {
        EndpointName = "tenant-service-tenant-join-que";
    }
}
public class DbCreatedWorkerDefinition : ConsumerDefinition<DbCreatedWorker>
{
    public DbCreatedWorkerDefinition()
    {
        EndpointName = "tenant-service-db-created-que";
    }
}
public class SchemaMigrationUpdatedWorkerDefinition : ConsumerDefinition<SchemaMigrationUpdatedWorker>
{
    public SchemaMigrationUpdatedWorkerDefinition()
    {
        EndpointName = "tenant-service-migration-success-que";
    }
}
public class UserInvitedWorkerDefinition : ConsumerDefinition<UserInvitedWorker>
{
    public UserInvitedWorkerDefinition()
    {
        EndpointName = "tenant-service-user-invited-que";
    }
}

public class InvitationAcceptedWorkerDefinition : ConsumerDefinition<InvitationAcceptedWorker>
{
    public InvitationAcceptedWorkerDefinition()
    {
        EndpointName = "tenant-service-invitation-accepted-que";
    }
}
