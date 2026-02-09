
using Newtonsoft.Json;

namespace OnePunch.Notification.Core.Messaging;

public class SendTenantConfirmationEmailHandler : IMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMQPublisher _publisher;
    private const string _nextEvent = "tenant.confirmation.email.sent";
    public SendTenantConfirmationEmailHandler(IServiceScopeFactory scopeFactory, IRabbitMQPublisher publisher)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    public async Task Handle(string message)
    {
        var model = ObjectSerializer.DeSerialized<RMQPayload<NotifTenantForConfirmation>>(message);
        if (model == null)
        {
            Log.Logger.Error("Unable to deserialize tenant confirmation email payload");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
        var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();
        var notifService = scope.ServiceProvider.GetRequiredService<EmailNotificationService>();

        if (await outboxService.IsEventExists(model.EventId)) return;

        try
        {
            await SendConfirmationEmailAsync(notifService, model);
            await PublishSuccessAsync(uow, model, outboxService);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to send tenant confirmation email");
            throw;
        }
    }

    private static async Task SendConfirmationEmailAsync(EmailNotificationService notifService, RMQPayload<NotifTenantForConfirmation> model)
    {
        var mailPayload = new Domain.DTO.MailPayload(model.Data.Email, model.Data.Token);
        notifService.SendTenantConfirmation(mailPayload, model.Data.ConfirmationRoute);
        await Task.CompletedTask;
    }

    private async Task PublishSuccessAsync(IUnitOfWorkService uow, RMQPayload<NotifTenantForConfirmation> model, OutBoxService outboxService)
    {
        var payload = new RMQPayload<NoticationResponse>
        {
            EventId = Guid.NewGuid(),
            CausationId = model.EventId,
            CorrelationId = model.CorrelationId,
            Data = new NoticationResponse { Message = "Success" }
        };

        var serializedMsg = ObjectSerializer.Serialized (payload);
        // Outbox entry for success
        var outbox = outboxService.CreateModel(
            model.Data.TenantId,
            model.Data.TenantId,
            payload.EventId,
            payload.CausationId,
            payload.CorrelationId,
            "TenantConfirmation",
            _nextEvent,
            OutBoxState.PROCESSING,
            serializedMsg
        );
        await outboxService.AddAsync(outbox);
        await uow.CommitChangesAsync();
        await _publisher.PublishAsync(serializedMsg, _nextEvent);
    }

}