using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

public class TenantCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _conf;

    public TenantCleanupWorker(IServiceScopeFactory scopeFactory, IConfiguration conf)
    {
        _scopeFactory = scopeFactory;
        _conf = conf;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Move configuration outside the loop (no need to parse it every 5 seconds)
        var retentionDays = -1;
        if (int.TryParse(_conf["TenantRetensionBeforeDelete"], out var configValue))
        {
            retentionDays = -Math.Abs(configValue);
        }

        Log.Logger.Information("Tenant Cleanup Worker started. Retention: {Days} days.", Math.Abs(retentionDays));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TenantService>();
                var cutoffDate = DateTime.UtcNow.AddDays(retentionDays);
                var unverifiedTenants = await db.Context.Tenants
                    .Where(m => m.Status == "Pending" && m.CreatedAt <= cutoffDate)
                    .OrderBy(m => m.CreatedAt)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                if (!unverifiedTenants.Any())
                {
                    // NO WORK: Wait a long time (e.g., 1 hour) before checking again
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                    continue;
                }

                await db.DeleteAsync(unverifiedTenants, stoppingToken);
                await db.CommitChangesAsync(stoppingToken);
                Log.Logger.Information("Cleaned up {Count} unverified tenants.", unverifiedTenants.Count);
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }

            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Error in TenantCleanupWorker cycle.");
                // Wait before retrying after an error to avoid log spamming
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }
}