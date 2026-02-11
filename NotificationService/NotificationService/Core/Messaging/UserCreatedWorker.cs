using Confluent.Kafka;
using Microsoft.Extensions.Options;
namespace OnePunch.Notification.Core.Messaging;
public class UserCreatedWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    public UserCreatedWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaSettings> settings)
    {
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var conf = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = "tenant-notif-service-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SecurityProtocol = SecurityProtocol.Plaintext,
            EnableAutoCommit = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(conf).Build();
        consumer.Subscribe(_settings.Topics.UserCreated);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                if (result == null || result.IsPartitionEOF) continue;
                var model = ObjectSerializer.Deserialized<MessagePayload<UserEmailPayload>>(result.Message.Value);
                if (model == null)
                {
                    Log.Logger.Error("Unable to deserialize email payload");
                    return;
                }
                using var scope = _scopeFactory.CreateScope();
                var notifService = scope.ServiceProvider.GetRequiredService<EmailNotificationService>();
                try
                {
                    //var retryPolicy = Policy
                    //    .Handle<Exception>() // Or specific database/network exceptions
                    //    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    //    (exception, timeSpan, retryCount, context) =>
                    //    {
                    //        Log.Logger.Warning($"Retry {retryCount} for {model.Data.Email} due to {exception.Message}");
                    //    });
                    //await retryPolicy.ExecuteAsync(async () =>
                    //{
                    //    if (!response.result.Succeeded)
                    //        throw new Exception("Registration failed: " + string.Join(", ", response.result.Errors.Select(e => e.Description)));
                    //});
                    var mailPayload = new Domain.DTO.MailPayload(model.Data.Email, model.Data.Token);
                    notifService.SendTenantConfirmation(mailPayload, model.Data.ConfirmationRoute);
                    await Task.CompletedTask;
                } 
                catch (ConsumeException ex)
                {
                    Log.Logger.Error(ex, "Kafka consumption error");
                    consumer.Commit(result);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "Failed to send tenant confirmation email");
                    consumer.Commit(result);
                } 
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Close(); // Cleanly leave the consumer group
        }
    }
}