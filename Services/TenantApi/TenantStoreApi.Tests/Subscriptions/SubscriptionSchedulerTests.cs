using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Quartz;
using Quartz.Impl.Triggers;
using TenantStoreApi.Core.Extensions;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>
/// The lifecycle runs as a Quartz job: one fire catches up activations, then moves every
/// subscription along its dates and saves it.
/// </summary>
public class SubscriptionLifecycleJobTests
{
    private static SubscriptionLifecycleJob JobOver(SubscriptionTestHost host)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => host.Lifecycle);
        services.AddScoped(_ => new SubscriptionBackfillService(host.Uow, host.Events, Options.Create(host.Options), host.Clock));
        return new SubscriptionLifecycleJob(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    private static IJobExecutionContext Fire()
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    [Fact]
    public async Task AFire_SavesTheStatusTheDatesSay()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(8));

        await JobOver(host).Execute(Fire());

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Expired);
    }

    [Fact]
    public async Task AFire_GivesAnUnbilledExistingOwnerTheLegacyPlan()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        host.AddCompany(owner);

        await JobOver(host).Execute(Fire());

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active);
    }

    [Fact]
    public void RunsOneFireAtATime()
    {
        typeof(SubscriptionLifecycleJob).IsDefined(typeof(DisallowConcurrentExecutionAttribute), false).Should().BeTrue();
    }
}

/// <summary>
/// Quartz is registered with a persistent, clustered store and an hourly cron trigger -- or not
/// at all when Subscriptions:SchedulerEnabled is false (before the QRTZ_ tables exist).
/// </summary>
public class SubscriptionSchedulerRegistrationTests
{
    private static IServiceCollection Register(Dictionary<string, string?> settings)
    {
        settings.TryAdd("ConnectionStrings:TenantConnection", "Server=localhost;Database=tenantstore;Uid=test;Pwd=test;");
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        SubscriptionRegistrations.AddSubscriptionScheduler(services, config);
        return services;
    }

    private static QuartzOptions QuartzOptionsOf(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IOptions<QuartzOptions>>().Value;

    [Fact]
    public void Disabled_RegistersNoScheduler()
    {
        var services = Register(new() { ["Subscriptions:SchedulerEnabled"] = "false" });

        services.Should().NotContain(d => d.ServiceType == typeof(ISchedulerFactory));
    }

    [Fact]
    public void ByDefault_TheJobRunsAtTheTopOfEveryHour_InUtc()
    {
        var options = QuartzOptionsOf(Register(new()));

        options.JobDetails.Should().ContainSingle(j => j.Key.Equals(SubscriptionLifecycleJob.Key) && j.Durable);
        var trigger = options.Triggers.Should().ContainSingle(t => t.Key.Equals(SubscriptionLifecycleJob.TriggerKey))
            .Which.Should().BeOfType<CronTriggerImpl>().Subject;
        trigger.Key.Should().Be(SubscriptionLifecycleJob.TriggerKey);
        trigger.JobKey.Should().Be(SubscriptionLifecycleJob.Key);
        trigger.CronExpressionString.Should().Be("0 0 * * * ?");
        trigger.TimeZone.Should().Be(TimeZoneInfo.Utc);
        trigger.MisfireInstruction.Should().Be(MisfireInstruction.CronTrigger.FireOnceNow);
    }

    [Fact]
    public void TheCronComesFromConfig()
    {
        var options = QuartzOptionsOf(Register(new() { ["Subscriptions:LifecycleCron"] = "0 30 * * * ?" }));

        options.Triggers.OfType<CronTriggerImpl>().Single(t => t.Key.Equals(SubscriptionLifecycleJob.TriggerKey))
            .CronExpressionString.Should().Be("0 30 * * * ?");
    }

    [Fact]
    public void TheMissedPaymentReconcileJob_RunsEveryFifteenMinutes()
    {
        var options = QuartzOptionsOf(Register(new()));

        options.JobDetails.Should().ContainSingle(j => j.Key.Equals(PaymentReconcileJob.Key) && j.Durable);
        options.Triggers.OfType<CronTriggerImpl>().Single(t => t.Key.Equals(PaymentReconcileJob.TriggerKey))
            .CronExpressionString.Should().Be("0 0/15 * * * ?");
    }

    [Fact]
    public void UsesAClusteredStore_WithTheQuartzConnection_OverTheTenantOne()
    {
        var options = QuartzOptionsOf(Register(new() { ["ConnectionStrings:Quartz"] = "Server=quartz;Database=jobs;" }));

        options["quartz.scheduler.instanceName"].Should().Be("tenantstore-scheduler");
        options["quartz.jobStore.clustered"].Should().Be("true");
        options["quartz.jobStore.tablePrefix"].Should().Be("QRTZ_");
        options["quartz.dataSource.default.connectionString"].Should().Be("Server=quartz;Database=jobs;");
    }
}
