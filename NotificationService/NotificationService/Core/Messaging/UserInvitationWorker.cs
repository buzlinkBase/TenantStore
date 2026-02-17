using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace OnePunch.Notification.Core.Messaging;

public class UserInvitationWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPollyPolicyFactory _pollyPolicyFactory;

    public UserInvitationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaSettings> settings,
        IPollyPolicyFactory pollyPolicyFactory)
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
            GroupId = "notification-service.user.invitation-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            // Allow enough time for email retries before Kafka thinks the consumer is dead
            MaxPollIntervalMs = 300000
        };

        using var consumer = new ConsumerBuilder<string, string>(conf)
            .SetErrorHandler((_, e) => Log.Error("Kafka Error: {Reason}", e.Reason))
            .Build();

        consumer.Subscribe(_settings.Topics.SendUserInvitation);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    result = consumer.Consume(stoppingToken);
                    if (result?.Message == null) continue;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error consuming invitation message. Retrying in 5s...");
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                try
                {
                    // 1. Poison Pill Check
                    var model = ObjectSerializer.Deserialize<MessagePayload<UserInvitionNotificationPayload>>(result.Message.Value);
                    if (model == null)
                    {
                        Log.Error("Invalid invitation format at {Offset}. Skipping.", result.Offset);
                        consumer.Commit(result);
                        continue;
                    }

                    var policy = _pollyPolicyFactory.GetHttpPolicy("reg.user.created.notif");
                    // 2. Resilience Execution
                    await policy.ExecuteAsync(async (ct) =>
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
                            AppName = model.Data.AppName ?? "Erp system",
                            Expiry = model.Data.Expiry,
                        };
                        // Ensure your service accepts the CancellationToken
                        await notifService.SendUserInvites(mailPayload, userInfo, stoppingToken);

                    }, stoppingToken);

                    // 3. Success: Move the offset forward
                    consumer.Commit(result);
                }
                catch (BrokenCircuitException)
                {
                    // Email provider is likely down. DO NOT COMMIT.
                    Log.Warning("Invitation circuit is OPEN. Backing off 30s. Offset {Offset} will be retried.", result.Offset);
                    await Task.Delay(30000, stoppingToken);
                }
                catch (Exception ex)
                {
                    // If we reach here, Polly has already tried several times and failed.
                    Log.Fatal(ex, "Permanent failure sending invitation for {Email} at {Offset}.", result.Message.Key, result.Offset);

                    // To avoid data loss, we do NOT commit. The message stays in Kafka.
                    // This creates 'Backpressure' which is safer than losing invitations.
                    await Task.Delay(60000, stoppingToken);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }
}