using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Onepunch.Common.Lib;

public interface IMessageHandlerFactory
{
    IMessageHandler Create(string routingKey);
}
public interface IMessageHandler
{
    Task Handle(string message);
}

public class MessageHandlerFactory : IMessageHandlerFactory
{
    private readonly IServiceProvider _serviceProvider;
    public MessageHandlerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IMessageHandler? Create(string routingKey)
    {
        var handler = _serviceProvider.GetKeyedService<IMessageHandler>(routingKey);
        return handler ?? throw new InvalidOperationException($"No IMessageHandler registered for key '{routingKey}'.");
        //return handler ?? throw new InvalidOperationException($"No IMessageHandler registered for key '{routingKey}'.");
    }
}
