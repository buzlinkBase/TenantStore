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
    private readonly IPollyPolicyFactory _pollyPolicyFactory;

    public UserConfirmedWorker(
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
        // Avoid blocking the startup sequence
        await Task.Yield();

        var conf = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = "tenant-service.admin.user.confirmed.group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // We handle commits manually
            EnablePartitionEof = false
        };

        using var consumer = new ConsumerBuilder<string, string>(conf)
            .SetErrorHandler((_, e) => Log.Error("Kafka Error: {Reason}. Fatal: {IsFatal}", e.Reason, e.IsFatal))
            .Build();

        consumer.Subscribe(_settings.Topics.TenantUserConfirmed);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    // 1. Block and wait for a message
                    result = consumer.Consume(stoppingToken);
                    if (result?.Message == null) continue;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error consuming from Kafka. Retrying in 5s...");
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                try
                {
                    // 2. Deserialize with Poison Pill check
                    var model = ObjectSerializer.Deserialize<MessagePayload<TenantUserPayload>>(result.Message.Value);
                    if (model == null)
                    {
                        Log.Error("Poison Pill detected! Unreadable payload at offset {Offset}. Skipping.", result.Offset);
                        consumer.Commit(result);
                        continue;
                    }

                    // 3. Resilient Execution
                    // NOTE: Use a policy specifically for Database/Logic, not a generic "HttpPolicy"
                    var policy = _pollyPolicyFactory.GetHttpPolicy("user.confirmed");

                    await policy.ExecuteAsync(async (ct) =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var tenantService = scope.ServiceProvider.GetRequiredService<TenantService>();

                        var tenant = await tenantService.FindTenantAsync(model.Data.TenantId, ct);
                        if (tenant == null)
                        {
                            Log.Warning("Tenant {TenantId} not found. Nothing to activate.", model.Data.TenantId);
                            return;
                        }

                        tenant.Status = "Active";
                        await tenantService.CommitChangesAsync(ct);

                    }, stoppingToken);

                    // 4. ONLY Commit on Success
                    consumer.Commit(result);
                }
                catch (BrokenCircuitException)
                {
                    Log.Error("Circuit is OPEN. The database is likely down. Backing off 30s...");
                    // We DO NOT commit here. We want to try this same message again later.
                    await Task.Delay(30000, stoppingToken);
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Critical failure at offset {Offset}. Worker pausing to prevent data loss.", result.Offset);
                    // CRITICAL: By not committing, we "block" the consumer. 
                    // This is safer than losing data. Manual intervention may be needed.
                    await Task.Delay(60000, stoppingToken);
                }
            }
        }
        finally
        {
            Log.Information("Closing Kafka Consumer...");
            consumer.Close(); // Ensures offsets are committed if configured, and group rebalance is triggered
        }
    }
}