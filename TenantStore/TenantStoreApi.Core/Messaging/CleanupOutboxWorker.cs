using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

public class CleanupOutboxWorker  : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public CleanupOutboxWorker(IServiceScopeFactory scopeFactory) 
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Logger.Information("Outbox Cleanup Worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbService = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
                var unverifiedTenants = await dbService.Context.OutboxMessages
                    .Where(m => m.Status == OutBoxState.PROCESSED
                        || m.ProcessedOn != null)
                    .OrderBy(m => m.CreatedAt)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                if (!unverifiedTenants.Any())
                {
                    await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
                    continue;
                }
                dbService.Repository.RemoveRange(unverifiedTenants);
                await dbService.CommitChangesAsync(stoppingToken);
                Log.Logger.Information("Cleaned up {Count} outbox.", unverifiedTenants.Count);
                await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
            }

            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Error in TenantCleanupWorker cycle.");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }
}