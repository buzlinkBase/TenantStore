
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi;

public class UserConfirmedHandler : IMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    public UserConfirmedHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task Handle(string message)
    {
        try
        {
            var model = JsonConvert.DeserializeObject<RMQPayload<UserCreatedPayload>>(message);
            if (model == null) return;
            using var scope = _scopeFactory.CreateScope();

            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
            var tenantService = scope.ServiceProvider.GetRequiredService<TenantService>();
            var service = scope.ServiceProvider.GetRequiredService<OutBoxService>();

            if (await service.IsEventExists(model.EventId)) return;

            var outbox = await service.FindEvent(model.CausationId);
            if (outbox != null)
            {
                outbox.Status = OutBoxState.PROCESSED;
                await service.AddAsync(outbox);
            }
            var tenant = await tenantService.FindTenant(model.Data.TenantId);
            if (tenant == null) return;
            tenant.Status = "For Confirmation";
            uow.CommitChanges();

        }
        catch (Exception ex)
        {
        }
    }
}
