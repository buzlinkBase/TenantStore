using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Onepunch.Auth.Core.Messaging;

public class AuthTenantConsumerWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PasswordCrypto _crypto;

    public AuthTenantConsumerWorker(IServiceScopeFactory scopeFactory,
        IOptions<KafkaSettings> settings,
        PasswordCrypto crypto)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _crypto = crypto;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var conf = new ConsumerConfig
        {
            GroupId = "tenant-api.admin.user.created",
            BootstrapServers = _settings.BootstrapServers,
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
                // Timeout after 1 second so the loop can check stoppingToken
                var result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result == null) continue;
                try
                {
                    var model = ObjectSerializer.DeSerialized<MessagePayload<TenantCreatedPayload>>(result.Message.Value);
                    if (model == null)
                    {
                        consumer.Commit(result);
                        Log.Logger.Error("No payload from tenant registration");
                        return;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var uow  = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
                    var userService = scope.ServiceProvider.GetRequiredService<UserService>();
                    model.Data.Password = _crypto.Decrypt(model.Data.Password);
                    var response = await userService.RegisterTenantAdmin(model.Data);
                    if (!response.result.Succeeded) continue;

                    //prepare payload for user notif
                    var token = GenerateEmailToken(model.Data);
                    StoreToken(uow, response.user, token);
                    var messPayload = ComposePayload(model, token);
                    var serializedMessage = ObjectSerializer.Serialized(messPayload);

                    var current = outboxService.CreateModel(
                        model.Data.TenantId,
                        user.Id,
                        messPayload.EventId,
                        messPayload.CausationId,
                        messPayload.CorrelationId,
                        "ForConfirmationTenant",
                        _nextEvent,
                        OutBoxState.PROCESSING,
                        serializedMessage);

                    await outboxService.AddAsync(current);
                    await Uow.CommitChangesAsync();

                    //generate email token
                    //store token
                    //create outbox

                    var serializedMessage = await CreateSuccessOutbox(model, response.user, uow, outboxService);
                    uow.CommitChanges();




                    //using (var scope = _scopeFactory.CreateScope())
                    //{
                    //    var  service   = scope.ServiceProvider.GetRequiredService<UserService>();
                    //    var tenantInfo  = JsonSerializer.Deserialize<MessagePayload<TenantCreatedPayload>>(result.Message.Value, options);
                    //    if (tenantInfo == null)
                    //    {
                    //        Log.Logger.Error($"unable to deserialized tenant info {result.Message.Value}");
                    //        consumer.Commit(result);
                    //    }

                    //    service.RegisterTenantAdmi(new TenantCreatedPayload()
                    //    {
                    //        Email= tenantInfo.Data.Email,
                    //        Password= tenantInfo.Data.Password,
                    //    })

                    //}
                }
                catch (Exception ex)
                {
                    //unable to deserialized
                    Log.Logger.Error(ex.Message);
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

    private void StoreToken(IUnitOfWorkService uow,
        User user, 
        string token)
    {
        var tokenModel = new EmailToken
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            Email = user.Email,
            Expiry = DateTime.UtcNow.AddDays(2),
            TokenType = "TenantConfirmation",
            TokenValue = token,
        };
        uow.Context.EmailTokens.Add(tokenModel);
    }

    private string GenerateEmailToken(TenantCreatedPayload model) => TokenGenerator.Generate(model.TenantId, model.Email);

    private MessagePayload<NotifTenantForConfirmation> ComposePayload(MessagePayload<TenantForConfirmation> model, string token)
    {
        return new MessagePayload<NotifTenantForConfirmation>
        {
            Data = new NotifTenantForConfirmation
            {
                Token = token,
                Email = model.Data.Email,
                TenantId = model.Data.TenantId,
                UserId = model.Data.UserId,
                Expiry = DateTime.UtcNow.AddDays(2),
                IssuedAt = DateTime.UtcNow,
                Purpose = "Tenant account confirmation",
                ConfirmationRoute = string.Concat(_domainProvider.Resolve().Apis.Bio, "/api/v1/user/confirm-email")
            }
        };
    }
}
