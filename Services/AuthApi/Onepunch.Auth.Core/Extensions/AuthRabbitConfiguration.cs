using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
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
            x.AddConsumer<TenantCreatedWorker, TenantCreatedConsumerDefinition>();
            x.AddConsumer<UserJoinTenantCreatedWorker, UserJoinConsumerDefinition>();
            x.AddConsumer<HrDbCreatedWorker, HrDbCreatedConsumerDefinition>();
            x.AddConsumer<MembershipChangedWorker, MembershipChangedConsumerDefinition>();
            x.AddEntityFrameworkOutbox<AuthContext>(o =>
            {
                o.UseMySql();
                o.UseBusOutbox();
            });
            x.SetEndpointNameFormatter(KebabCaseEndpointNameFormatter.Instance);
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.UseMessageRetry(r =>
                {
                    r.Exponential(5, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));
                });
                cfg.UseCircuitBreaker(cb =>
                {
                    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                    cb.TripThreshold = 15; // Trip after 15 failures
                    cb.ResetInterval = TimeSpan.FromMinutes(5); // Wait 5 mins before trying again
                });
                cfg.UsePublishFilter(typeof(TenantPublishFilter<>), context);
                //cfg.UseConsumeFilter(typeof(TenantConsumeFilter<>), context);
                //cfg.Host(settings.Host, settings.VirtualHost, h =>
                //{
                //    h.Username(settings.Username);
                //    h.Password(settings.Password);
                //});
                cfg.Host(settings.Uri);
                //cfg.ConfigurePublish(p => p.UseExecute(c => c.SetPersistent()));
                //cfg.ConfigureSend(s => s.UseExecute(c => c.SetPersistent()));
                cfg.SetQuorumQueue();
                cfg.ConfigureEndpoints(context);
            });
        });
    }
}

public class TenantCreatedConsumerDefinition : ConsumerDefinition<TenantCreatedWorker>
{
    public TenantCreatedConsumerDefinition()
    {
        EndpointName = "auth-tenant-created-que";
    }

}
public class UserJoinConsumerDefinition : ConsumerDefinition<UserJoinTenantCreatedWorker>
{
    public UserJoinConsumerDefinition()
    {
        EndpointName = "auth-user-join-que";
    }
}
public class HrDbCreatedConsumerDefinition : ConsumerDefinition<HrDbCreatedWorker>
{
    public HrDbCreatedConsumerDefinition()
    {
        EndpointName = "auth-hrdb-created-que";
    }
}
public class MembershipChangedConsumerDefinition : ConsumerDefinition<MembershipChangedWorker>
{
    public MembershipChangedConsumerDefinition()
    {
        EndpointName = "auth-membership-changed-que";
    }
}