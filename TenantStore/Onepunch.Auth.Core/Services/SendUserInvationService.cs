using Onepunch.Auth.Core.Providers;
using Onepunch.Auth.Domain.Entities;
namespace OnePunch.Auth.Core.Services;

public class SendUserInvationService : BaseService<OutboxMessage>
{
    private readonly IRabbitMQPublisher _publisher;
    private readonly OutBoxService _outboxService;
    private readonly IAuthDomainProvider _domainProvider;
    private const string _nextEvent = "notif.send.user.invitation";
    public SendUserInvationService(
        IUnitOfWorkService uow,
        IRabbitMQPublisher publisher,
        OutBoxService outboxService,
        IAuthDomainProvider domainProvider) : base(uow)
    {
        _publisher = publisher;
        _outboxService = outboxService;
        _domainProvider = domainProvider;
    }

    public async Task Send(Guid tenantId, string tenantName, string name, string email)
    {
        var token = TokenGenerator.Generate(tenantId, email);
        var emailToken = new EmailToken
        {
            EventId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            Email = email,
            TenantId = tenantId,
            EventType="user.invitation",
            TokenType = "UserInvite",
            TokenValue = token,
            Status = "Active",
            Expiry = DateTime.UtcNow.AddDays(7),
        };
        Context.EmailTokens.Add(emailToken);
        CommitChanges();

        var messPayload = ComposePayload(emailToken, tenantName, name);
        var serializedMessage = ObjectSerializer.Serialized(messPayload);
        await _publisher.PublishAsync(serializedMessage, _nextEvent);
    }

    private MessagePayload<UserEmailPayload> ComposePayload(
        EmailToken model,
        string tenantName,
        string username )
    {
        return new MessagePayload<UserEmailPayload>
        {
            EventId = model.EventId,
            CausationId = model.CausationId!.Value,
            CorrelationId = model.CorrelationId!.Value,
            EventType = _nextEvent,
            Data = new UserEmailPayload
            {
                Token = model.TokenValue,
                Email = model.Email,
                TenantName = tenantName,
                FullName = username,
                Expiry = DateTime.UtcNow.AddDays(2),
                IssuedAt = DateTime.UtcNow,
                Purpose = "User invitation",
                ConfirmationRoute = string.Concat(_domainProvider.Resolve().Frontends.Bio, $"/register")
            }
        };
    }
}

