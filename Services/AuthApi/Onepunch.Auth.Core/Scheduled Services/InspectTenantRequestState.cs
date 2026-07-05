using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Onepunch.Auth.Core.Scheduled_Services;

public class InspectTenantRequestStateWatcher : BackgroundService
{
    private readonly TimeSpan _loopDelay = TimeSpan.FromMinutes(10);
    private readonly IServiceScopeFactory _factory;

    public InspectTenantRequestStateWatcher(IServiceScopeFactory factory)
    {
        _factory = factory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _factory.CreateScope())
                {
                    var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
                    var service = scope.ServiceProvider.GetRequiredService<TenantRequestService>();
                    var data = await service.FindExpired();

                    if (data != null && data.Any())
                    {
                        foreach (var tenant in data)
                        {
                            var payload = new DbStateUpdatePayload
                            {
                                TenantId = tenant.TenantId,
                                IsActive = false,
                                ServiceOwner = "hrms",
                                Remarks = "Expired",
                                ArchieveSchedule = null,
                                Status = "Deactivated"
                            };
                            await publisher.Publish(payload, context =>
                            {
                                context.Headers.Set("X-Tenant-ID", tenant.TenantId.ToString());
                                context.CorrelationId = tenant.TenantId;
                            });
                        }
                        await service.CommitChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in getting expired tenant request {0}", ex.Message);
            }
            await Task.Delay(_loopDelay, stoppingToken);
        }
    }
}