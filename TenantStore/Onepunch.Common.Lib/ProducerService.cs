using Confluent.Kafka;
using Serilog;

namespace Onepunch.Common.Lib;

public class ProducerService : IDisposable
{
    private readonly IProducer<string, string> _producer;
    public ProducerService(string BootstrapServers)
    {
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = BootstrapServers,
            AllowAutoCreateTopics = true,

            // Reliability Settings
            EnableIdempotence = true,
            Acks = Acks.All,

            // Timeouts
            MessageTimeoutMs = 5000,
            LingerMs = 5, // Small delay to batch messages together for better throughput

            // Retries (Idempotent producer handles retries automatically and better)
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 100,
        };

        _producer = new ProducerBuilder<string, string>(producerConfig)
            .SetErrorHandler((_, e) => Log.Error("Kafka Producer Error: {Reason}", e.Reason))
            .Build();
    }

    public async Task ProduceAsync(string key, string topic, string message, CancellationToken ct = default)
    {
        try
        {
            var kafkaMessage = new Message<string, string> { Key = key, Value = message };

            // ProduceAsync returns a DeliveryResult
            var deliveryResult = await _producer.ProduceAsync(topic, kafkaMessage, ct);

            if (deliveryResult.Status != PersistenceStatus.Persisted)
            {
                throw new Exception($"Message was not persisted to Kafka. Status: {deliveryResult.Status}");
            }
        }
        catch (ProduceException<string, string> ex)
        {
            Log.Error(ex, "Failed to deliver message to Kafka topic {Topic} for key {Key}. Error: {Error}",
                topic, key, ex.Error.Reason);
            throw; // Re-throw so the OutboxWorker knows to retry
        }
    }

    public void Dispose()
    {
        // Important: Flush the producer to send any buffered messages before app exit
        _producer?.Flush(TimeSpan.FromSeconds(10));
        _producer?.Dispose();
    }
}