using Confluent.Kafka;


namespace Kf;

public class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaProducerService _producer;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, KafkaProducerService producer)
    {
        _scopeFactory = scopeFactory;
        _producer = producer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            //using var scope = _scopeFactory.CreateScope();
            //var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();

            //// Get pending messages
            //var messages = await db.OutboxMessages
            //    .Where(m => m.ProcessedAt == null && m.ErrorCount < 5)
            //    .Take(20)
            //    .ToListAsync();

            //foreach (var msg in messages)
            //{
            //    try
            //    {
            //        await _producer.ProduceAsync(msg.Topic, msg.Content);

            //        msg.ProcessedAt = DateTime.UtcNow; // Mark as done
            //    }
            //    catch (Exception)
            //    {
            //        msg.ErrorCount++; // Retry later
            //    }
            //}

            //await db.SaveChangesAsync();
            await Task.Delay(5000, stoppingToken); // Wait 5 seconds before next poll
        }
    }
}