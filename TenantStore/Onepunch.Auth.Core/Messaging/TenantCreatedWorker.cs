using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using Polly.CircuitBreaker;
using Serilog;

namespace Onepunch.Auth.Core.Messaging;

public class TenantCreatedWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPollyPolicyFactory _pollyPolicy;
    private readonly Domains _domainOptions;

    public TenantCreatedWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<Domains> domainOptions,
        IOptions<KafkaSettings> settings,
        IPollyPolicyFactory pollyPolicy)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _pollyPolicy = pollyPolicy;
        _domainOptions = domainOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Offload from the startup thread immediately
        await Task.Yield();

        var conf = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = "user-service-admin-user.create",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // Manual commit for consistency
        };

        using var consumer = new ConsumerBuilder<string, string>(conf)
         .SetLogHandler((_, log) => Log.Information("KAFKA LOG: {Message}", log.Message))
         .SetErrorHandler((_, e) => Log.Error("KAFKA ERROR: {Reason}", e.Reason))
         .Build();

        consumer.Subscribe(_settings.Topics.TenantCreated);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                if (result == null || result.IsPartitionEOF) continue;

                try
                {
                    // 1. Deserialization
                    var model = ObjectSerializer.Deserialize<MessagePayload<TenantCreatedPayload>>(result.Message.Value);
                    if (model?.Data == null || string.IsNullOrWhiteSpace(model.Data.Email))
                    {
                        Log.Warning("Poison Pill detected at offset {Offset}. Skipping.", result.TopicPartitionOffset);
                        consumer.Commit(result);
                        continue;
                    }

                    string plainPassword;
                    using (var initScope = _scopeFactory.CreateScope())
                    {
                        var crypto = initScope.ServiceProvider.GetRequiredService<PasswordCrypto>();
                        plainPassword = crypto.Decrypt(model.Data.Password);
                    }

                    // 3. Resilient Execution
                    var policy = _pollyPolicy.GetHttpPolicy("User-Registration");
                    await policy.ExecuteAsync(async () =>
                    {
                        // Every retry gets a FRESH scope and FRESH DbContext
                        using var scope = _scopeFactory.CreateScope();

                        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
                        var emailTokenService = scope.ServiceProvider.GetRequiredService<EmailTokenService>();
                        var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();

                        // Apply the plain password to the model for this specific registration attempt
                        model.Data.Password = plainPassword;

                        // Transactional Logic
                        var response = await userService.RegisterTenantAdmin(model.Data);
                        if (!response.result.Succeeded)
                        {
                            var errors = string.Join(", ", response.result.Errors.Select(e => e.Description));
                            throw new Exception($"DB Registration failed: {errors}");
                        }

                        var user = response.user;
                        var exp = DateTime.UtcNow.AddDays(7);

                        // Token Logic
                        var tokenModel = await emailTokenService.CreateModelAsync("tenant.created", exp, model.Data.Email);
                        tokenModel.UserId = user.Id;
                        await emailTokenService.StoreToken(tokenModel);

                        // Outbox Logic
                        var tokenMsg = ObjectSerializer.Serialize(tokenModel);
                        var messPayload = ComposePayload(user, tokenMsg);
                        var serializedMessage = ObjectSerializer.Serialize(messPayload);

                        var outboxEntry = outboxService.CreateModel(
                            model.Data.TenantId,
                            user.Id.ToString(),
                            _settings.Topics.UserCreated,
                            serializedMessage);

                        await outboxService.AddAsync(outboxEntry, stoppingToken);

                        // Finalize Transaction
                        await userService.CommitChangesAsync(stoppingToken);
                    });

                    // 4. Success - Move Kafka Offset
                    consumer.Commit(result);
                }
                catch (BrokenCircuitException)
                {
                    // Circuit is open. We stop processing for a bit.
                    // IMPORTANT: We do NOT commit 'result' here, so it stays in Kafka.
                    Log.Error("Circuit is OPEN. Backing off 10s before retrying offset {Offset}", result.TopicPartitionOffset);
                    await Task.Delay(10000, stoppingToken);
                }
                catch (Exception ex)
                {
                    // If we reached here, Polly retries were exhausted or a non-retriable error occurred.
                    Log.Fatal(ex, "Permanent failure at offset {Offset}. Manual intervention needed.", result.TopicPartitionOffset);

                    // Option: You could commit here to skip the message, or keep it uncommitted to block the partition.
                    // consumer.Commit(result); 
                }
            }
        }
        catch (OperationCanceledException) { /* Normal shutdown */ }
        finally
        {
            consumer.Close();
        }
    }
    private MessagePayload<UserEmailPayload> ComposePayload(User user, string token)
    {
        return new MessagePayload<UserEmailPayload>
        {
            Data = new UserEmailPayload
            {
                Token = token,
                Email = user.Email!,
                TenantId = user.TenantId,
                UserId = user.Id,
                Expiry = DateTime.UtcNow.AddDays(2),
                IssuedAt = DateTime.UtcNow,
                Purpose = "Tenant account confirmation",
                ConfirmationRoute = $"{_domainOptions.AuthDomain}/api/v1/user/confirm-email"
            }
        };
    }
}