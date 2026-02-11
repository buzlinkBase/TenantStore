using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnePunch.Auth.Core;
using Serilog;

namespace Onepunch.Auth.Core;

public class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public OutboxWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
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
                Log.Logger.Error(ex, "Error occurred while processing outbox messages.");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
        var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();
        var _producer = scope.ServiceProvider.GetRequiredService<ProducerService>();
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
                msg.Remarks = null;
                msg.Status = OutBoxState.PROCESSED;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Failed to publish outbox message {Id}: {Message}", msg.Id, ex.Message);
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