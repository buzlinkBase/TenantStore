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
    private readonly Domains _domainOptions;
    private readonly PollyPolicy _pollyPolicy;

    public TenantCreatedWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<Domains> domainOptions,
        IOptions<KafkaSettings> settings,
        PollyPolicy pollyPolicy)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _domainOptions = domainOptions.Value;
        _pollyPolicy = pollyPolicy;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var conf = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = "user-service-admin-user.create-group2",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        //using var consumer = new ConsumerBuilder<string, string>(conf).Build();
        using var consumer = new ConsumerBuilder<string, string>(conf)
        .SetErrorHandler((_, e) => Log.Error($"Kafka Error: {e.Reason}"))
        .SetStatisticsHandler((_, json) => Log.Debug($"Statistics: {json}"))
        .SetLogHandler((_, m) => Log.Information($"Kafka Log: {m.Message}"))
        .Build();
        consumer.Subscribe(_settings.Topics.TenantCreated);


        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(TimeSpan.FromMilliseconds(100));
                if (result == null || result.IsPartitionEOF) continue;

                //var result = consumer.Consume(stoppingToken);
                //if (result == null || result.IsPartitionEOF) continue;

                try
                {
                    // 1. Deserialization (Poison Pill Guard)
                    var model = ObjectSerializer.Deserialize<MessagePayload<TenantCreatedPayload>>(result.Message.Value);
                    if (model?.Data == null || string.IsNullOrWhiteSpace(model.Data.Email))
                    {
                        Log.Logger.Warning("Invalid payload received. Skipping offset {Offset}", result.TopicPartitionOffset);
                        consumer.Commit(result);
                        continue;
                    }

                    // 2. Resilient Transactional Work
                    await _pollyPolicy.WrapPolicy.ExecuteAsync(async () =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var emailTokenService  = scope.ServiceProvider.GetRequiredService<EmailTokenService>();
                        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
                        var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();
                        var crypto = scope.ServiceProvider.GetRequiredService<PasswordCrypto>();
                        var exp = DateTime.UtcNow.AddDays(7);

                        // Decrypt (Note: Ensure this is idempotent or doesn't break on retry)
                        var decryptedPassword = crypto.Decrypt(model.Data.Password);
                        model.Data.Password = decryptedPassword;

                        // Register
                        var response = await userService.RegisterTenantAdmin(model.Data);
                        if (!response.result.Succeeded)
                        {
                            var errors = string.Join(", ", response.result.Errors.Select(e => e.Description));
                            // We throw a standard Exception to trigger the Polly Retry
                            throw new Exception($"Registration failed for {model.Data.Email}: {errors}");
                        }

                        // Token & Outbox (Part of the same DB transaction)
                        var user = response.user;

                        //create and store token
                        var tokenModel  = await emailTokenService.CreateModelAsync("tenant.created", exp, model.Data.Email);
                        tokenModel.UserId=user.Id;
                        await emailTokenService.StoreToken(tokenModel);

                        //prepare email payload
                        var tokenMsg = ObjectSerializer.Serialize(tokenModel);
                        var messPayload = ComposePayload(user, tokenMsg);
                        var serializedMessage = ObjectSerializer.Serialize(messPayload);

                        //store outbox
                        var outboxEntry = outboxService.CreateModel(
                            model.Data.TenantId,
                            user.Id.ToString(),
                            _settings.Topics.UserCreated,
                            serializedMessage);
                        await outboxService.AddAsync(outboxEntry);

                        // Finalize DB Transaction
                        await userService.CommitChangesAsync();
                    });

                    // 3. Commit Kafka Offset only on success
                    consumer.Commit(result);
                }
                catch (BrokenCircuitException)
                {
                    Log.Logger.Error("Auth DB/Service circuit is OPEN. Backing off 10s...");
                    await Task.Delay(10000, stoppingToken);
                }
                catch (Exception ex)
                {
                    Log.Logger.Fatal(ex, "Permanent failure for message at offset {Offset}", result.TopicPartitionOffset);
                    // Decide: discard or keep? Usually, we commit and send to a DLQ/Logs for manual fix.
                    //consumer.Commit(result);
                }
            }
        }
        catch (OperationCanceledException) { }
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