using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core;

public class UserConfirmedWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;

    public UserConfirmedWorker(IServiceScopeFactory scopeFactory,
        IOptions<KafkaSettings> settings)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Important: Move this logic into the main flow
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
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var tenantService = scope.ServiceProvider.GetRequiredService<TenantService>();
                        var model = ObjectSerializer.Deserialized<MessagePayload<TenantUserPayload>>(result.Message.Value);
                        if (model == null)
                        {
                            Log.Logger.Error($"unable to deserialized admin user {result.Message.Value}");
                            consumer.Commit(result);
                        }
                        var tenant = await tenantService.FindTenant(model.Data.TenantId);
                        //TODO retry logic here via poly
                        if (tenant == null)
                        {
                            Log.Logger.Error($"unable to load tenant {model.Data}:{result.Message.Value}");
                            consumer.Commit(result);
                        }
                        if (tenant != null)
                        {
                            tenant.Status = "Active";
                        } 
                        await tenantService.CommitChangesAsync();
                    }
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
}
