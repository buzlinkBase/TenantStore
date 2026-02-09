using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;
using System.Text;

namespace Onepunch.Common.Lib;

public class RabbitMQConsumer : BackgroundService
{
    private readonly RabbitMQSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private IChannel channel;
    public RabbitMQConsumer(
        IOptions<RabbitMQSettings> options,
        IServiceScopeFactory scopeFactory)
    {
        _settings = options.Value;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _settings.Host,
                Port = _settings.Port,
                UserName = _settings.Username,
                Password = _settings.Password,
                AutomaticRecoveryEnabled = true
            };

            var connection = await factory.CreateConnectionAsync();
            channel = await connection.CreateChannelAsync();

            await channel.QueueDeclareAsync(
                queue: _settings.OnepunchQue,
                durable: true,
                exclusive: false,
                autoDelete: false
            );

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += Consumer_ReceivedAsync;
            await channel.BasicConsumeAsync(
                queue: _settings.OnepunchQue,
                autoAck: false,
                consumer: consumer
            );

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

            await channel.CloseAsync();
            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex.Message);
        }
    }

    private async Task Consumer_ReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var body = ea.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);
        var routingKey = ea.RoutingKey;
        var deliveryTag = ea.DeliveryTag;
        using var scope = _scopeFactory.CreateScope();
        try
        {
            var handlerFactory = scope.ServiceProvider.GetRequiredService<IMessageHandlerFactory>();
            var handler = handlerFactory.Create(routingKey);
            await handler.Handle(message);
            await channel.BasicAckAsync(deliveryTag, multiple: false);
            Log.Logger.Information($"Message processed: {routingKey} | {deliveryTag} {message}");
        }
        catch (Exception ex)
        {
            var msg = $"Error processing message{routingKey} - {ex.Message}";
            Log.Logger.Error(msg);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
        }
    }
}