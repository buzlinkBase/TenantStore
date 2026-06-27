using MassTransit;


namespace OnePunch.Notification.Core.Messaging;

public class SignInGoogleWorker : IConsumer<SignInGoogleEmail>
{
    private readonly EmailNotificationService _service;

    public SignInGoogleWorker(EmailNotificationService service) 
    {
        _service = service;
    }
    public async Task Consume(ConsumeContext<SignInGoogleEmail> context)
    {
        var message  = context.Message;
        await _service.SendSignInGoogleInformation(message, context.CancellationToken);
    }
}