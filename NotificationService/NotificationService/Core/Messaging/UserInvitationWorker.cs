using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace OnePunch.Notification.Core.Messaging;

public class UserInvitationWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PollyPolicy _pollyPolicy;

    public UserInvitationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaSettings> settings,
        PollyPolicy pollyPolicy)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _pollyPolicy = pollyPolicy;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var conf = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = "notification-service:user.invitation-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // We handle commits manually for reliability
            // SecurityProtocol = SecurityProtocol.Plaintext // Configure as needed
        };

        using var consumer = new ConsumerBuilder<string, string>(conf).Build();
        consumer.Subscribe(_settings.Topics.SendUserInvitation);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // 1. Consume message (Blocks until message arrives or timeout/cancel)
                var result = consumer.Consume(stoppingToken);
                if (result == null || result.IsPartitionEOF) continue;

                try
                {
                    // 2. Deserialize (The "Poison Pill" check)
                    var model = ObjectSerializer.Deserialize<MessagePayload<UserInvitionNotificationPayload>>(result.Message.Value);
                    if (model == null)
                    {
                        Log.Logger.Error("Invalid message format at {Offset}. Skipping.", result.TopicPartitionOffset);
                        consumer.Commit(result);
                        continue;
                    }
                    // 3. Execute with Resilience Policy
                    await _pollyPolicy.WrapPolicy.ExecuteAsync(async () =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var notifService = scope.ServiceProvider.GetRequiredService<EmailNotificationService>();
                        var mailPayload = new Domain.DTO.MailPayload(model.Data.Email, model.Data.Token);
                        var userInfo = new UserEmailPayload
                        {
                            TenantName = model.Data.TenantName ?? model.Data.AppName ?? "",
                            ConfirmationRoute = model.Data.InviteLink,
                            Email = model.Data.Email,
                            FullName = model.Data.Name ?? "User",
                            AppName = model.Data.AppName ?? "app",
                            Expiry = model.Data.Expiry,
                        };
                        await notifService.SendUserInvites(mailPayload, userInfo);
                    });
                    // 4. Commit ONLY after successful processing
                    consumer.Commit(result);
                }
                catch (BrokenCircuitException)
                {
                    // DO NOT COMMIT. Let the message stay in Kafka.
                    Log.Logger.Error("Circuit is OPEN. Backing off 10s. Message at {Offset} will be retried.", result.TopicPartitionOffset);
                    await Task.Delay(10000, stoppingToken);
                }
                catch (Exception ex)
                {
                    // Final catch for this specific message
                    Log.Logger.Fatal(ex, "Permanent failure for message at {Offset}.", result.TopicPartitionOffset);
                    // TODO Option: Move to a Dead Letter Topic here. 
                    // For now, we commit to prevent blocking the whole queue.
                    consumer.Commit(result);
                }
            }
        }
        catch (OperationCanceledException) { /* Clean shutdown */ }
        finally
        {
            consumer.Close();
        }
    }
}