using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly.CircuitBreaker;
using Serilog;

namespace TenantStoreApi.Core.Messaging;

public class TenantCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PollyPolicy _pollyPolicy;

    public TenantCleanupWorker(IServiceScopeFactory scopeFactory, PollyPolicy pollyPolicy)
    {
        _scopeFactory = scopeFactory;
        _pollyPolicy = pollyPolicy;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        Log.Logger.Information("Tenant Cleanup Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool hasMoreWork = await _pollyPolicy.WrapPolicy.ExecuteAsync(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
                    // FIX: If CreatedAt + 20 days is GREATER than Now, it's a new tenant.
                    // You want to delete where it is LESS than Now (meaning 20 days have passed).
                    var cutoffDate = DateTime.UtcNow.AddDays(-20);
                    var unverifiedTenants = await db.Context.Tenants
                        .Where(m => m.Status == "Pending" && m.CreatedAt <= cutoffDate)
                        .OrderBy(m => m.CreatedAt)
                        .Take(50)
                        .ToListAsync(stoppingToken);

                    if (!unverifiedTenants.Any())
                        return false;

                    db.Context.RemoveRange(unverifiedTenants);
                    await db.CommitChangesAsync();
                    Log.Logger.Information("Cleaned up {Count} unverified tenants.", unverifiedTenants.Count);
                    return true;
                });

                // If no work was found, wait longer (e.g., 1 hour)
                // If work was found, wait just a few seconds to avoid DB pressure but keep moving.
                var delayTime = hasMoreWork ? TimeSpan.FromSeconds(5) : TimeSpan.FromHours(1);
                await Task.Delay(delayTime, stoppingToken);
            }
            catch (BrokenCircuitException)
            {
                Log.Logger.Error("Cleanup circuit open. Backing off for 30 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Error during tenant cleanup cycle.");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Back off on unknown errors
            }
        }
    }
}