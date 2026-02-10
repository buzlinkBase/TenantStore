
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi;

public class AdminUserCreatedHandler : IMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMQPublisher _publisher;
    private const string _nextEventType = "tenant.for.confirmation";

    public AdminUserCreatedHandler(IServiceScopeFactory scopeFactory,
        IRabbitMQPublisher publisher)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
    }

    public async Task Handle(string message)
    {
        try
        {
            var model = ObjectSerializer.DeSerialized<MessagePayload<UserCreatedPayload>>(message);
            if (model == null) return;

            using var scope = _scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
            var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();
            var tenantService = scope.ServiceProvider.GetRequiredService<TenantService>();

            //load outbox
            if (await outboxService.IsEventExists(model.EventId)) return;

            var outbox = await outboxService.FindEvent(model.CausationId);
            if (outbox != null) await outboxService.UpdateStateAsync(outbox, OutBoxState.PROCESSED);

            //UPDATE TENANT STATE
            var tenant = await tenantService.FindTenant(model.Data.TenantId);
            if (tenant == null) throw new Exception("tenant not found");
            tenant.Status = "For Confirmation";
            uow.Context.Tenants.Update(tenant);

            // Create next outbox event
            var data = new MessagePayload<TenantForConfirmation>
            {
                EventId = Guid.NewGuid(),
                CausationId = model.EventId,
                EventType = "tenant.for.confirmation",
                Data = new TenantForConfirmation
                {
                    Email = model.Data.Email,
                    TenantId = model.Data.TenantId,
                    UserId = model.Data.UserId,
                }
            };
            var serrializedMessage = ObjectSerializer.Serialized(data);
            //create outbox
            var confirmationOutbox = outboxService.CreateModel(tenant.Id, tenant.Id,
                data.EventId,
                data.CausationId,
                data.CorrelationId,
                "ForConfirmationTenant",
                _nextEventType, OutBoxState.PROCESSING, serrializedMessage);
            await outboxService.AddAsync(confirmationOutbox);
            // Commit everything 
            uow.CommitChanges();

            await _publisher.PublishAsync(serrializedMessage, _nextEventType);

        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to process tenant confirmation");
            throw;
        }
    }
}
