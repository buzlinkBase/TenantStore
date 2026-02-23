using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Providers;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using Polly.CircuitBreaker;
using Serilog;

namespace Onepunch.Auth.Core.Messaging;

public class TenantCreatedWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ITenantProvider _tenantProvider;
    private readonly IPollyPolicyFactory _pollyPolicy;
    private readonly Domains _domainOptions;
    private readonly ConsumerConfig _config;

    public TenantCreatedWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<Domains> domainOptions,
        IOptions<KafkaSettings> settings,
        IConfiguration configuration,
        IPollyPolicyFactory pollyPolicy)
    {
        _settings = settings.Value;
        _config = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = "user-service-admin-user.create-v2",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // Manual commit
            // Safety: Ensure we don't block forever if the broker is unreachable
            //SocketTimeoutMs = 30000,
            //SessionTimeoutMs = 30000
        };

        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _pollyPolicy = pollyPolicy;
        _domainOptions = domainOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // FIX 1: Use Task.Run so the Host can finish starting up. 
        // Without this, the app "hangs" on the first Consume() call.
        await ProcessKafkaMessages(stoppingToken);
    }

    private async Task ProcessKafkaMessages(CancellationToken stoppingToken)
    {
        // FIX 2: Move builder inside the Task.Run to ensure it's on the background thread
        using var consumer = new ConsumerBuilder<string, string>(_config)
            .SetLogHandler((_, log) => Log.Information("KAFKA LOG: {Message}", log.Message))
            .SetErrorHandler((_, e) => Log.Error("KAFKA ERROR: {Reason}", e.Reason))
            .Build();

        consumer.Subscribe(_settings.Topics.TenantCreated);
        Log.Information("TenantCreatedWorker subscribed to: {Topic}", _settings.Topics.TenantCreated);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    // FIX 3: Consume with a timeout or the stoppingToken to prevent infinite hang
                    result = consumer.Consume(stoppingToken);

                    if (result == null || result.IsPartitionEOF)
                        continue;

                    // 1. Deserialization
                    var model = ObjectSerializer.Deserialize<MessagePayload<TenantCreatedPayload>>(result.Message.Value);
                    if (model?.Data == null || string.IsNullOrWhiteSpace(model.Data.Email))
                    {
                        Log.Warning("Poison Pill detected at offset {Offset}. Skipping.", result.TopicPartitionOffset);
                        consumer.Commit(result);
                        continue;
                    }

                    // 2. Logic execution inside Polly
                    //var policy = _pollyPolicy.GetHttpPolicy("User-Registration");
                    //await policy.ExecuteAsync(async () =>
                    //{
                    //});
                    //// 4. Success - Commit Offset
                    ///
                    using var scope = _scopeFactory.CreateScope();

                    //set tenant
                    var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                    var tenantprovider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
                    tenantAccessor.SetTenantId(model.Data.TenantId);
                    tenantprovider.SetTenantId(tenantAccessor.GetTenantId());

                    //resolve services  
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
                    var userService = scope.ServiceProvider.GetRequiredService<UserService>();
                    var crypto = scope.ServiceProvider.GetRequiredService<PasswordCrypto>();
                    var emailTokenService = scope.ServiceProvider.GetRequiredService<EmailTokenService>();
                    var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();

                    // 3. Business logic
                    string plainPassword = crypto.Decrypt(model.Data.Password);
                    model.Data.Password = plainPassword;
                    var response = await userService.RegisterTenantAdmin(model.Data);
                    if (!response.result.Succeeded)
                    {
                        var errors = string.Join(", ", response.result.Errors.Select(e => e.Description));
                        throw new Exception($"DB Registration failed: {errors}");
                    }

                    var exp = DateTime.UtcNow.AddDays(2);
                    var tokenModel = await emailTokenService.CreateModelAsync("user.created", exp, model.Data.Email);
                    tokenModel.UserId = response.user.Id;
                    await emailTokenService.StoreToken(tokenModel, stoppingToken);

                    //compose email payload
                    var emailDomain = ComposePayload(response.user, tokenModel);
                    var outboxEntry = outboxService.CreateModel(
                        model.Data.TenantId,
                        response.user.Id.ToString(),
                        _settings.Topics.UserCreated,
                        ObjectSerializer.Serialize(emailDomain));

                    await outboxService.AddAsync(outboxEntry, stoppingToken);
                    await uow.CommitChangesAsync(stoppingToken);
                    consumer.Commit(result);

                }
                catch (OperationCanceledException)
                {
                    Log.Warning("Kafka Consumer stopping due to application shutdown.");
                    break;
                }
                catch (BrokenCircuitException)
                {
                    Log.Error("Circuit is OPEN. Backing off 10s at offset {Offset}", result?.TopicPartitionOffset);
                    await Task.Delay(10000, stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    Log.Error(ex, "Kafka consume error (connection issue?)");
                    await Task.Delay(2000, stoppingToken); // Don't tight-loop on connection errors
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Critical error processing message at offset {Offset}", result?.TopicPartitionOffset);
                    // Decide: Commit to skip (DLQ logic) or wait for manual fix?
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private MessagePayload<UserEmailPayload> ComposePayload(User user, CreateEmailToken tokenInfo)
    {
        var token = tokenInfo.TokenValue;
        return new MessagePayload<UserEmailPayload>
        {
            Data = new UserEmailPayload
            {
                Token = token,
                Email = user.Email!,
                TenantId = user.TenantId,
                UserId = user.Id,
                TenantName = tokenInfo?.TenantName ?? "",
                AppName = _configuration["AppName"] ?? "OnePunch",
                FullName = user.Name ?? "User",
                Expiry = tokenInfo?.Expiry ?? DateTime.UtcNow.AddDays(2),
                IssuedAt = DateTime.UtcNow,
                Purpose = "Tenant account confirmation",
                ConfirmationRoute = $"{_domainOptions.AuthDomain}/api/v1/users/confirm-email?token={token}"
            }
        };
    }
}