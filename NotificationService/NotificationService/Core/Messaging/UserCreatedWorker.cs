using Confluent.Kafka;
using MassTransit;
using Polly.CircuitBreaker;

namespace OnePunch.Notification.Core.Messaging;

public class UserCreatedWorker : IConsumer<UserEmailPayload>
{
    private readonly EmailNotificationService _emailNotificationService;
    public UserCreatedWorker(
        EmailNotificationService emailNotificationService )
    {
        _emailNotificationService = emailNotificationService;
    }
    public async Task Consume(ConsumeContext<UserEmailPayload> context)
    {
        var model = context.Message; 
        var mailPayload = new Domain.DTO.MailPayload(model.Email, model.Token);
        await _emailNotificationService.SendTenantConfirmationAsync(mailPayload, model.ConfirmationRoute, context.CancellationToken);
    } 
}