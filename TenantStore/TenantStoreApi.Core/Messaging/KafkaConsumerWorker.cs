using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Kf;

public class KafkaConsumerWorker : BackgroundService
{
    private readonly IConfiguration _config;

    public KafkaConsumerWorker(IOptions< config) => _config = config;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Important: Move this logic into the main flow
        await Task.Yield();
        var conf = new ConsumerConfig
        {
            GroupId = "webapi-kf-v2", // Change the GroupId to force a fresh read
            BootstrapServers = _config["Kafka:BootstrapServers"],
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SecurityProtocol = SecurityProtocol.Plaintext,
            EnableAutoCommit = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(conf).Build();
        consumer.Subscribe(_config["Kafka:TopicName"]);

        // Use a try-finally to ensure the consumer closes correctly
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Timeout after 1 second so the loop can check stoppingToken
                var result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result == null) continue;

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var user = JsonSerializer.Deserialize<User>(result.Message.Value, options);

                if (user != null)
                {
                    Console.WriteLine($"[CONSUMER] Received: {user.Username}");
                 consumer.Commit(result);
                }
            }
        }
        catch (OperationCanceledException) { /* Normal shutdown */ }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL] Consumer Loop Died: {ex.Message}");
        }
    }
}
public class User
{
    public string Username { get; set; }
}