using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TenantStoreApi.Core;
public class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProducerService _producer;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(
        IServiceScopeFactory scopeFactory,
        ProducerService producer,
        ILogger<OutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _producer = producer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing outbox messages.");
            }
            // Polling interval - keep it short (e.g., 2-5 seconds)
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
        // 1. Fetch only what is ready for retry or never processed
        var messages = await db.Context.OutboxMessages
            .Where(m => m.ProcessedOn == null
                        && m.RetryCount < 10
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
                msg.Remarks = null; // Clear previous errors on success
                msg.Status = OutBoxState.PROCESSED;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to publish outbox message {Id}: {Message}", msg.Id, ex.Message);
                msg.RetryCount++;
                msg.LastAttemptOn = DateTime.UtcNow;
                msg.Remarks = ex.Message;
                msg.Status = OutBoxState.RETRY;
                // 2. Exponential Backoff: Wait longer after each failure
                // Attempt 1: 1 min, Attempt 2: 4 mins, Attempt 3: 9 mins...
                msg.NextRetryOn = DateTime.UtcNow.AddMinutes(Math.Pow(msg.RetryCount, 2));
            }
        }
        // 3. Save all status updates at once
        await db.SaveChangesAsync();
    }
}