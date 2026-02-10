
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi;

public class TenantEmailConfirmedHandler : IMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMQPublisher _publisher;
    private const string _nextEventType = "tenant.activated";

    public TenantEmailConfirmedHandler(IServiceScopeFactory scopeFactory,
        IRabbitMQPublisher publisher)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
    }

    public async Task Handle(string message)
    {
        try
        {
            var model = ObjectSerializer.DeSerialized<MessagePayload<UserActivatedPayload>>(message);
            if (model == null) return;

            using var scope = _scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
            var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();
            var tenantService = scope.ServiceProvider.GetRequiredService<TenantService>(); 
            //close event
            if (await outboxService.IsEventExists(model.EventId)) return;
            var outbox = await outboxService.FindEvent(model.CausationId);
            if (outbox != null) await outboxService.UpdateStateAsync(outbox, OutBoxState.PROCESSED);

            //update tenant
            var tenant = await tenantService.FindTenant(model.Data.TenantId);
            if (tenant == null) throw new Exception("tenant not found");
            tenant.Status = "Active";
            uow.Repository.Update(tenant);
            uow.CommitChanges();

            //ack
            var data = new MessagePayload<TenantActivatdPayload>
            {
                EventId = Guid.NewGuid(),
                CausationId = model.EventId,
                EventType = _nextEventType,
                Data = new TenantActivatdPayload
                {
                    Email = model.Data.Email,
                    TenantId = model.Data.TenantId,
                    UserId = model.Data.UserId,
                }
            };
            var serrializedMessage = ObjectSerializer.Serialized(data);
            await _publisher.PublishAsync(serrializedMessage, _nextEventType);

        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to process tenant confirmation");
            throw;
        }
    }
}
