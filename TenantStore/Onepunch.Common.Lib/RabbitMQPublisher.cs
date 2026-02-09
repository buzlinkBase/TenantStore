using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;


namespace Onepunch.Common.Lib;


public interface IRabbitMQPublisher
{
    Task PublishAsync(string message, string routingKey);
}

public class RabbitMQPublisher : IRabbitMQPublisher
{
    private readonly RabbitMQSettings _settings;

    public RabbitMQPublisher(IOptions<RabbitMQSettings> options)
    {
        _settings = options.Value;
    }

    public async Task PublishAsync(string message, string routingKey)
    {
        var factory = new ConnectionFactory
        {
            HostName = _settings.Host,
            Port = _settings.Port,
            UserName = _settings.Username,
            Password = _settings.Password,
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(_settings.Exchange, ExchangeType.Direct, durable: true);
        await channel.QueueDeclareAsync(_settings.OnepunchQue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(_settings.OnepunchQue, _settings.Exchange, routingKey);
        var body = Encoding.UTF8.GetBytes(message);
        await channel.BasicPublishAsync(_settings.Exchange, routingKey, body);

    }
}
