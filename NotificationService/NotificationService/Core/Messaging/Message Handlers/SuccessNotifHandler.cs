namespace OnePunch.Notification.Core.Messaging;
public class SuccessNotifHandler : IMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    public SuccessNotifHandler(IServiceScopeFactory scopeFactory, IRabbitMQPublisher publisher)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task Handle(string message)
    {
        var model = ObjectSerializer.DeSerialized<RMQPayload<NoticationResponse>>(message);
        if (model == null)
        {
            Log.Logger.Error($"Unable to deserialize {nameof(SuccessNotifHandler)}");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
        var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();
        if (await outboxService.IsEventExists(model.EventId))
            return;
        var prior = await outboxService.FindEvent(model.CausationId);
        if (prior != null)
        {
            await outboxService.UpdateStateAsync(prior, OutBoxState.PROCESSED);
            await uow.CommitChangesAsync();
        }
    }
}