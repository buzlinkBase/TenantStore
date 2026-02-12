using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using Serilog;

namespace Onepunch.Auth.Core.Messaging;

public class TenantCreatedWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Domains _domainOptions; 
    public TenantCreatedWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<Domains> domainOptions ,
        IOptions<KafkaSettings> settings
        )
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _domainOptions = domainOptions.Value; 
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var conf = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = "user-service-admin-user.create-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SecurityProtocol = SecurityProtocol.Plaintext,
            EnableAutoCommit = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(conf).Build();
        consumer.Subscribe(_settings.Topics.TenantCreated);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result == null || result.IsPartitionEOF) continue;
                    var model = ObjectSerializer.Deserialized<MessagePayload<TenantCreatedPayload>>(result.Message.Value);
                    if (model?.Data == null || string.IsNullOrWhiteSpace(model.Data.Email))
                    {
                        Log.Logger.Warning("Invalid payload received: {Payload}", result.Message.Value);
                        consumer.Commit(result); // Commit so we don't get stuck on a "poison" message
                        continue;
                    }

                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
                        var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();
                        var _crypto = scope.ServiceProvider.GetRequiredService<PasswordCrypto>();

                        // 1. Decrypt & Register
                        model.Data.Password = _crypto.Decrypt(model.Data.Password);
                        // Define a policy: Retry 3 times with a delay of 2, 4, and 8 seconds
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
                        var response = await userService.RegisterTenantAdmin(model.Data);
                        if (!response.result.Succeeded)
                        {
                            var errors = string.Join(", ", response.result.Errors.Select(e => e.Description));
                            Log.Logger.Error("Registration failed for {Email}: {Errors}", model.Data.Email, errors);
                            //TODO implement retry logic here via POLY
                            // Decide here: Commit to skip, or don't commit to retry? Skipping for now.
                            consumer.Commit(result);
                            continue;
                        }

                        // 2. Token & Outbox Logic
                        var user = response.user;
                        var token = GenerateEmailToken(model.Data);
                        StoreToken(userService.UnitOfWork, user, token);

                        var messPayload = ComposePayload(user, token);
                        var serializedMessage = ObjectSerializer.Serialized(messPayload);

                        //create outbox
                        var outboxEntry = outboxService.CreateModel(
                            model.Data.TenantId,
                            user.Id.ToString(),
                            _settings.Topics.UserCreated,
                            serializedMessage);
                        await outboxService.AddAsync(outboxEntry);
                        await userService.CommitChangesAsync();
                        consumer.Commit(result);
                    }
                }
                catch (ConsumeException ex)
                {
                    Log.Logger.Error(ex, "Kafka consumption error");
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "Error processing tenant creation message");
                    // We don't commit here so the message is retried, 
                    // though in production you'd want a retry limit/DLQ.
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Close(); // Cleanly leave the consumer group
        }
    }

    private void StoreToken(IUnitOfWorkService uow, User user, string token)
    {
        var tokenModel = new EmailToken
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            Email = user.Email!,
            Expiry = DateTime.UtcNow.AddDays(2),
            TokenType = "TenantConfirmation",
            TokenValue = token,
        };
        uow.Context.EmailTokens.Add(tokenModel);
    }

    private string GenerateEmailToken(TenantCreatedPayload model) =>
        TokenGenerator.Generate(model.TenantId, model.Email);

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