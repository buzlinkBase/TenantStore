using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Serilog;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core;

public class UserConfirmedWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PollyPolicy _pollyPolicy;

    public UserConfirmedWorker(
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
        // Yield to let the startup finish before blocking on Kafka
        await Task.Yield();

        var conf = new ConsumerConfig
        {
            GroupId = "tenant-api.admin.user.confirmed",
            BootstrapServers = _settings.BootstrapServers,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // Manual commits for consistency
        };

        using var consumer = new ConsumerBuilder<string, string>(conf).Build();
        consumer.Subscribe(_settings.Topics.TenantCreated);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Use the stoppingToken directly in Consume for better efficiency
                var result = consumer.Consume(stoppingToken);
                if (result == null || result.IsPartitionEOF) continue;

                try
                {
                    // 1. Deserialize (Poison Pill Check)
                    var model = ObjectSerializer.Deserialize<MessagePayload<TenantUserPayload>>(result.Message.Value);
                    if (model == null)
                    {
                        Log.Logger.Error("Unable to deserialize admin user: {Payload}", result.Message.Value);
                        consumer.Commit(result);
                        continue;
                    }

                    // 2. Resilient Execution Wrap
                    await _pollyPolicy.WrapPolicy.ExecuteAsync(async () =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var tenantService = scope.ServiceProvider.GetRequiredService<TenantService>();
                        var tenant = await tenantService.FindTenant(model.Data.TenantId);
                        if (tenant == null)
                        {
                            // If business logic says this is a permanent "Not Found" error, 
                            // we log and exit the policy so we can commit/skip.
                            Log.Logger.Warning("Tenant {TenantId} not found. Skipping.", model.Data.TenantId);
                            return;
                        }
                        tenant.Status = "Active";
                        await tenantService.CommitChangesAsync();
                    });
                    // 3. Commit only on Success
                    consumer.Commit(result);
                }
                catch (BrokenCircuitException)
                {
                    Log.Logger.Error("Database/Service circuit is OPEN. Backing off 10s...");
                    await Task.Delay(10000, stoppingToken);
                }
                catch (Exception ex)
                {
                    Log.Logger.Fatal(ex, "Permanent error processing UserConfirmed at offset {Offset}", result.TopicPartitionOffset);
                    // Commit to move past the failing message after all retries failed
                    consumer.Commit(result);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Close();
        }
    }
}