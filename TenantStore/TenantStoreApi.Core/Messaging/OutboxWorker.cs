using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace TenantStoreApi.Core;

public class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public OutboxWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Error occurred while processing outbox messages.");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
        var _producer = scope.ServiceProvider.GetRequiredService<ProducerService>();
        var messages = await db.Context.OutboxMessages
            .Where(m => m.ProcessedOn == null
                        && (m.RetryForever || m.RetryCount < 10)
                        && (m.NextRetryOn == null || m.NextRetryOn <= DateTime.UtcNow))
            .OrderBy(m => m.CreatedAt)
            .Take(50)
            .ToListAsync(stoppingToken);

        if (!messages.Any()) return;

        foreach (var msg in messages)
        {
            try
            {
                await _producer.ProduceAsync(msg.Key, msg.Topic, msg.Payload);
                msg.ProcessedOn = DateTime.UtcNow;
                msg.Remarks = string.Empty;
                msg.Status = OutBoxState.PROCESSED;
            }
            catch (Exception ex)
            {
                var retryPow = msg.RetryCount > 10 ? 10 : Math.Pow(msg.RetryCount, 2);
                Log.Logger.Warning("Failed to publish outbox message {Id}", msg.Id);
                msg.RetryCount++;
                msg.LastAttemptOn = DateTime.UtcNow;
                msg.Remarks = ex.Message;
                msg.Status = OutBoxState.RETRY;
                msg.NextRetryOn = DateTime.UtcNow.AddMinutes(retryPow);
            }
        }
        await db.CommitChangesAsync(stoppingToken);

    }
}