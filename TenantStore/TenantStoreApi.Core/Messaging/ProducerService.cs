using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace TenantStoreApi.Core;
public class ProducerService
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaSettings _settings;
    public ProducerService(IOptions<KafkaSettings> settings)
    {
        _settings = settings.Value;
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            AllowAutoCreateTopics = true,
            EnableIdempotence = true,
            Acks=Acks.All,
            MessageTimeoutMs = 5000,
            SocketTimeoutMs = 5000,
            MessageSendMaxRetries = 2,
            QueueBufferingMaxMessages = 1000
        };
        _producer = new ProducerBuilder<string, string>(producerConfig).Build();
    }
    public async Task ProduceAsync(string key, string topic, string message)
    {
        await _producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = message });
    }
}