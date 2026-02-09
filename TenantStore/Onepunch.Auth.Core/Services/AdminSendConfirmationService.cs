using Onepunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Services;
public class AdminSendConfirmationService
{
    private readonly OutBoxService _outBoxService;
    private readonly IRabbitMQPublisher _rabbitMQPublisher;
    private const string _nextEvent = "admin.user.email.confirmed";

    public AdminSendConfirmationService(
        OutBoxService outBoxService,
        IRabbitMQPublisher rabbitMQPublisher)
    {
        _outBoxService = outBoxService;
        _rabbitMQPublisher = rabbitMQPublisher;
    }

    public async Task HandleFailure(RegistrationResult result, EmailToken? emailToken)
    {
        await Task.CompletedTask;
    }
    public async Task HandleSuccess(RegistrationResult result, EmailToken emailToken)
    {
        var payload = new RMQPayload<UserActivatedPayload>
        {
            EventId = Guid.NewGuid(),
            CausationId = emailToken.EventId,
            Data = new UserActivatedPayload
            {
                Email = emailToken.Email,
                TenantId = Guid.NewGuid(),
                UserId = emailToken.UserId!.Value,
            },
        };

        var msg = ObjectSerializer.Serialized(payload);
        var outbox = _outBoxService.CreateModel(
            emailToken.TenantId,
            emailToken.Id,
            payload.EventId,
            payload.CausationId,
            payload.CorrelationId,
            "UserSuccessConfirmation",
            _nextEvent,
            OutBoxState.PROCESSING,
            msg
            );
        await _rabbitMQPublisher.PublishAsync(msg, _nextEvent);
    }
}
