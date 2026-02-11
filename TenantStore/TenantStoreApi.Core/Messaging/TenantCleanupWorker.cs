using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TenantStoreApi.Core.Messaging;
public class TenantCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public TenantCleanupWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Yield();
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
                        var unverifiedTenants = await db.Context.Tenants
                        .Where(m => m.Status == "Pending" &&
                        (m.CreatedAt.AddDays(20) >= DateTime.UtcNow))
                        .OrderBy(m => m.CreatedAt)
                        .Take(50)
                        .ToListAsync(stoppingToken);
                        if (!unverifiedTenants.Any()) return;
                        db.Context.RemoveRange(unverifiedTenants);
                        await db.CommitChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                }
            }
        }
        catch (Exception ex)
        {
        }
    }
}
