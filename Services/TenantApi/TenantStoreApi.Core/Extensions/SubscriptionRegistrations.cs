using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.Common.Lib.Subscriptions;
using Quartz;
using Refit;
using TenantStoreApi.Core.Services.Subscriptions;

namespace TenantStoreApi.Core.Extensions;

/// <summary>
/// Subscription plans and entitlements. The *Service classes are picked up by
/// RegisterCoreServices' convention scan; this adds the rest -- options, the state machine, the
/// usage providers (Composite) and the hourly lifecycle job on Quartz.
/// </summary>
public static class SubscriptionRegistrations
{
    public static void AddSubscriptions(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services.Configure<SubscriptionOptions>(config.GetSection(SubscriptionOptions.Section));
        services.AddSingleton(TimeProvider.System);
        // Factory, not the type: DI would pick the greedy IEnumerable<ISubscriptionState> constructor
        // (always resolvable, but empty here) and every StateOf lookup would throw KeyNotFound.
        services.AddSingleton(_ => new SubscriptionStateMachine());
        AddSubscriptionScheduler(services, config);

        services.AddScoped<ICompanyUsageProvider, AdminSeatUsageProvider>();
        AddRemoteUsageProvider(services, "hrms-api",
            config["Subscriptions:Usage:HrmsApiUrl"], config["Subscriptions:Usage:HrmsApiKey"],
            [
                EntitlementKeys.EmployeesMax,
                EntitlementKeys.PayrollCore, EntitlementKeys.PayrollLoansIncome, EntitlementKeys.ReportsStatutory,
                EntitlementKeys.ApprovalsMultilevel, EntitlementKeys.Announcements, EntitlementKeys.ClientsAgency,
            ]);
        AddRemoteUsageProvider(services, "hrms-adms-api",
            config["Subscriptions:Usage:AdmsApiUrl"], config["Subscriptions:Usage:AdmsApiKey"],
            [EntitlementKeys.BiometricDevicesMax]);
    }

    /// <summary>
    /// The lifecycle job on Quartz, the same way hrms-api runs its scheduled jobs (AddHrmsQuartz):
    /// a persistent, clustered job store in the tenantstore database (ConnectionStrings:Quartz,
    /// else TenantConnection) so each fire runs on exactly one instance and a fire missed while
    /// every instance was down runs once when one is back. Needs Quartz's standard QRTZ_ tables
    /// (Scripts/quartz-tables-mysql.sql). Subscriptions:SchedulerEnabled = false skips it all.
    /// </summary>
    public static void AddSubscriptionScheduler(IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(SubscriptionOptions.Section);
        if (!section.GetValue(nameof(SubscriptionOptions.SchedulerEnabled), true)) return;

        var connectionString = config.GetConnectionString("Quartz")
                               ?? config.GetConnectionString("TenantConnection")
                               ?? throw new InvalidOperationException("No connection string for the Quartz job store.");
        var cron = section.GetValue<string?>(nameof(SubscriptionOptions.LifecycleCron)) is { Length: > 0 } configured
            ? configured
            : new SubscriptionOptions().LifecycleCron;

        services.AddQuartz(q =>
        {
            q.SchedulerName = "tenantstore-scheduler";
            q.SchedulerId = "AUTO";
            q.UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.UseMySqlConnector(db =>
                {
                    db.ConnectionString = connectionString;
                    db.TablePrefix = "QRTZ_";
                });
                store.UseSystemTextJsonSerializer();
                store.UseClustering();
            });

            q.AddJob<SubscriptionLifecycleJob>(SubscriptionLifecycleJob.Key, job => job.StoreDurably());
            q.AddTrigger(trigger => trigger
                .ForJob(SubscriptionLifecycleJob.Key)
                .WithIdentity(SubscriptionLifecycleJob.TriggerKey)
                .WithCronSchedule(cron, schedule => schedule
                    .InTimeZone(TimeZoneInfo.Utc)
                    .WithMisfireHandlingInstructionFireAndProceed()));
        });
        services.AddQuartzHostedService(options =>
        {
            options.StartDelay = TimeSpan.FromSeconds(5);
            options.WaitForJobsToComplete = true;
        });
    }

    // Unconfigured: that service's counts stay unknown, so a downgrade that depends on them is
    // refused rather than waved through.
    private static void AddRemoteUsageProvider(
        IServiceCollection services, string name, string? baseUrl, string? apiKey, string[] limitKeys)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return;

        var clientName = $"subscription-usage:{name}";
        services.AddHttpClient(clientName, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
            if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Add("Api-key", apiKey);
        });
        services.AddScoped<ICompanyUsageProvider>(sp => new RemoteCompanyUsageProvider(
            name,
            RestService.For<ICompanyUsageApi>(sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName)),
            limitKeys));
    }
}
