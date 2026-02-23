using AutoMapper;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Serilog;

namespace DTR.Core.Messaging;

public class  TimeshiftWorker : BackgroundService
{
    private readonly KafkaSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConsumerConfig _config;

    public TimeshiftWorker(IServiceScopeFactory scopeFactory,
        IOptions<KafkaSettings> settings)
    {
        _settings = settings.Value;
        _config = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = "dtr-service-employee-setup-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };
        _scopeFactory = scopeFactory;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessKafkaMessages(stoppingToken);
    }
    private async Task ProcessKafkaMessages(CancellationToken stoppingToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(_config)
            .SetLogHandler((_, log) => Log.Information("KAFKA LOG: {Message}", log.Message))
            .SetErrorHandler((_, e) => Log.Error("KAFKA ERROR: {Reason}", e.Reason))
            .Build();
        consumer.Subscribe(_settings.Topics.TimeShift);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    result = consumer.Consume(stoppingToken);

                    if (result == null || result.IsPartitionEOF)
                        continue;

                    var model = ObjectSerializer.Deserialize<MessagePayload<TimeShiftPayload>>(result.Message.Value);
                    if (model?.Data == null)
                    {
                        Log.Warning($"Poison Pill detected: {nameof(TimeshiftWorker)}: Skipping.", result.TopicPartitionOffset);
                        consumer.Commit(result);
                        continue;
                    }
                    using var scope = _scopeFactory.CreateScope();
                    var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
                    var tsService = scope.ServiceProvider.GetRequiredService<TimeShiftService>();
                    var shift  = mapper.Map<TimeShift>(model);
                    tsService.AddOrUpdate(shift);
                    await tsService.SaveChangesAsync(stoppingToken);
                    await tsService.CommitChangesAsync(stoppingToken);
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
}