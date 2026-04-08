using MassTransit;

using OnePunch.Notification.Core.Messaging;
using OnePunch.Notification.Infrastructure;

namespace OnePunch.Notification.Core.Extensions;
public static class NotifTenantRabbitConfiguration
{
    public static void NotifConfigRabbitMq(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection("RabbitMqSettings").Get<RabbitMqSettings>();
        if (settings == null) return;
        builder.Services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<NotifContext>(o =>
            {
                o.UseMySql();
                o.UseBusOutbox();
                //o.QueryDelay = TimeSpan.FromSeconds(5);
                //o.DisableInboxCleanupService();
            });

            x.SetEndpointNameFormatter(KebabCaseEndpointNameFormatter.Instance);
            x.AddConsumer<AccountConfirmationWorker, AccountConfirmationDefinition>();
            x.AddConsumer<ResetPasswordWorker, ResetPasswordConsumerDefinition>();
            x.AddConsumer<UserInvitationWorker, UserInvitationConsumerDefinition>();

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
                //cfg.Host(settings.Host, settings.VirtualHost, h =>
                //{
                //    h.Username(settings.Username);
                //    h.Password(settings.Password);
                //});
                cfg.Host("amqps://lriumdis:PNHXZ9uy2nWDyLQC4yJQEtN5H8zMRUSm@armadillo.rmq.cloudamqp.com/lriumdis");
                cfg.SetQuorumQueue();
                cfg.ConfigureEndpoints(context);
            });
        });
    }
}

public class AccountConfirmationDefinition : ConsumerDefinition<AccountConfirmationWorker>
{
    public AccountConfirmationDefinition()
    {
        EndpointName = "notification-user-created-que";
    }
}

public class ResetPasswordConsumerDefinition : ConsumerDefinition<ResetPasswordWorker>
{
    public ResetPasswordConsumerDefinition()
    {
        EndpointName = "notification-reset-password-que";
    }
}

public class UserInvitationConsumerDefinition : ConsumerDefinition<UserInvitationWorker>
{
    public UserInvitationConsumerDefinition()
    {
        EndpointName = "notification-user-invitation-que";
    }
}

