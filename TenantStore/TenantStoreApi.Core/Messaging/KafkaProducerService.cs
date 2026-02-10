using Confluent.Kafka;

namespace Kf;

public class KafkaProducerService
{
    private readonly IConfiguration _config;
    private readonly IProducer<string, string> _producer;

    public KafkaProducerService(IConfiguration config)
    {
        _config = config;
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _config["Kafka:BootstrapServers"], 
            AllowAutoCreateTopics=true,
            EnableIdempotence = true
        };
        _producer = new ProducerBuilder<string, string>(producerConfig).Build();
    }

    public async Task ProduceAsync(string message)
    {
        var topic = _config["Kafka:TopicName"];
        await _producer.ProduceAsync(topic, new Message<string, string> { Key="testKey", Value = message });
    }
}