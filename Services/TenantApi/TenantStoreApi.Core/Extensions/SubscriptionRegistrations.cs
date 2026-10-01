using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.Common.Lib.Subscriptions;
using Refit;
using TenantStoreApi.Core.Services.Subscriptions;

namespace TenantStoreApi.Core.Extensions;

/// <summary>
/// Subscription plans and entitlements. The *Service classes are picked up by
/// RegisterCoreServices' convention scan; this adds the rest -- options, the state machine, the
/// usage providers (Composite) and the hourly lifecycle job.
/// </summary>
public static class SubscriptionRegistrations
{
    public static void AddSubscriptions(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services.Configure<SubscriptionOptions>(config.GetSection(SubscriptionOptions.Section));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SubscriptionStateMachine>();
        services.AddHostedService<SubscriptionLifecycleJob>();

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
