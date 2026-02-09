using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Onepunch.Auth.Core.Providers;
using OnePunch.Auth.Core.Services;

namespace OnePunch.Auth.Core.Messaging;

public class TenantEmailNotifSuccessHandler : IMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMQPublisher _publisher;
    private readonly IAuthDomainProvider _domainProvider;
    public TenantEmailNotifSuccessHandler(IServiceScopeFactory scopeFactory,
        IRabbitMQPublisher publisher,
        IAuthDomainProvider domainProvider)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _domainProvider = domainProvider;
    }

    public async Task Handle(string message)
    {
        var model = JsonConvert.DeserializeObject<RMQPayload<NoticationResponse>>(message);
        if (model == null) return;
        using var scope = _scopeFactory.CreateScope();

        var Uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();
        if (await outboxService.IsEventExists(model.EventId)) return;

        //stop email retry
        var payload = new RMQPayload<NoticationResponse>()
        {
            EventId = Guid.NewGuid(),
            CausationId = model.EventId,
            CorrelationId = model.CorrelationId,
            Data = new NoticationResponse { }
        };
        var serializedMsg = ObjectSerializer.Serialized(payload);
        await _publisher.PublishAsync(serializedMsg, "notif.success");
    }
}
