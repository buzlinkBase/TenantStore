using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using static Confluent.Kafka.ConfigPropertyNames;

namespace OnePunch.Notification.Core.Messaging;

public class UserCreatedWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPollyPolicyFactory _pollyPolicyFactory;

    public UserCreatedWorker(
        IServiceScopeFactory scopeFactory,
        IPollyPolicyFactory pollyPolicyFactory,
        IOptions<KafkaSettings> settings)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _pollyPolicyFactory = pollyPolicyFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var conf = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = "tenant-notif-service-admin-user.created-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // We handle commits manually for reliability
                                      // SecurityProtocol = SecurityProtocol.Plaintext // Configure as needed
        };

        using var consumer = new ConsumerBuilder<string, string>(conf)
            .SetErrorHandler((_, e) => Log.Error("Kafka Error: {Reason}. Fatal: {IsFatal}", e.Reason, e.IsFatal))
            .Build();
        consumer.Subscribe(_settings.Topics.UserCreated);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    // If Kafka is down, this line will block or throw an exception.
                    // No messages are lost; they stay on the Broker.
                    result = consumer.Consume(stoppingToken);
                    if (result?.Message == null) continue;

                    var model = ObjectSerializer.Deserialize<MessagePayload<UserEmailPayload>>(result.Message.Value);
                    if (model == null)
                    {
                        Log.Logger.Error("Poison Pill at {Offset}. Committing to skip.", result.Offset);
                        consumer.Commit(result); // Skip unreadable junk
                        continue;
                    }

                    var policy = _pollyPolicyFactory.GetHttpPolicy("tenant.user.created.notif");

                    await policy.ExecuteAsync(async (ct) =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var notifService = scope.ServiceProvider.GetRequiredService<EmailNotificationService>();
                        var mailPayload = new Domain.DTO.MailPayload(model.Data.Email, model.Data.Token);
                        await notifService.SendTenantConfirmationAsync(mailPayload, model.Data.ConfirmationRoute, ct);
                    }, stoppingToken);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) { break; }
                catch (BrokenCircuitException)
                {
                    Log.Logger.Error("Email provider is down. Backing off... (Not committing)");
                    await Task.Delay(15000, stoppingToken);
                    // result is NOT committed. On next loop, Consume() will get the SAME message.
                }
                catch (Exception ex)
                {
                    Log.Logger.Fatal(ex, "Business logic error. Pausing to prevent log spam.");
                    // DANGER REMOVED: We no longer Commit(result) here.
                    // If the code crashes, we want the message to stay in Kafka so we can fix the bug.
                    await Task.Delay(30000, stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
        }
        finally
        {
            consumer.Close();
        }
    }
}