using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
namespace OnePunch.Auth.Core.Messaging;

public class TenantActivatedHandler : IMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    public TenantActivatedHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }
    public async Task Handle(string message)
    {
        var model = JsonConvert.DeserializeObject<RMQPayload<TenantActivatdPayload>>(message);
        if (model == null) return;
        using var scope = _scopeFactory.CreateScope();
        var Uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
        var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();
        var prior = await outboxService.FindEvent(model.CausationId);
        if (prior != null && prior.Status != OutBoxState.PROCESSED)
        {
            prior.Status = OutBoxState.PROCESSED;
            Uow.CommitChanges();
        }
    }
}
