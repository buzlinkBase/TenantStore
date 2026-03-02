using MassTransit;
using Microsoft.Extensions.DependencyInjection;
namespace Onepunch.Common.Lib;
public interface IProducerService
{
    Task ProduceAsync<T>(T message, CancellationToken cancellationToken = default)
         where T : class, new();
}
public class KafkaProducerService : IProducerService
{
    private readonly IServiceProvider _serviceProvider;
    public KafkaProducerService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    public async Task ProduceAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var producer = _serviceProvider.GetRequiredService<ITopicProducer<T>>();
        await producer.Produce(message, cancellationToken);
    }
}