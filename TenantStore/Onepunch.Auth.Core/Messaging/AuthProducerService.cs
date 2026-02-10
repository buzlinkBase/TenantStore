using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Onepunch.Auth.Core;

public class AuthProducerService
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaSettings _settings;
    public AuthProducerService(IOptions<KafkaSettings> settings)
    {
        _settings = settings.Value;
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            AllowAutoCreateTopics = true,
            EnableIdempotence = true,
            Acks=Acks.All,
        };
        _producer = new ProducerBuilder<string, string>(producerConfig).Build();
    }
    public async Task ProduceAsync(string key, string topic, string message)
    {
        await _producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = message });
    }
}