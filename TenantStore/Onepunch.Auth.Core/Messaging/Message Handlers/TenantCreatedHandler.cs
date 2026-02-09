using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using Serilog;

namespace OnePunch.Auth.Core.Messaging;

public class TenantCreatedHandler : IMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMQPublisher _publisher;
    private readonly PasswordCrypto _crypto;
    private const string _nextEvent = "admin.user.created";

    public TenantCreatedHandler(
        IServiceScopeFactory scopeFactory,
        IRabbitMQPublisher publisher,
        PasswordCrypto crypto)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
    }

    public async Task Handle(string message)
    {
        Guard.ThrowIfNull(message, nameof(message));
        var model = DeserializeMessage(message);
        if (model == null) return;

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();
        if (await outboxService.IsEventExists(model.EventId)) return;

        model.Data.Password = _crypto.Decrypt(model.Data.Password);
        var response = await userService.RegisterTenantAdmin(model.Data);
        if (!response.result.Succeeded)
        {
            HandleFailure(response);
            return;
        }
        var serializedMessage = await CreateSuccessOutbox(model, response.user, uow, outboxService);
        uow.CommitChanges();
        await _publisher.PublishAsync(serializedMessage, _nextEvent);
    }

    private static RMQPayload<TenantCreatedPayload>? DeserializeMessage(string message)
    {
        try
        {
            return ObjectSerializer.DeSerialized<RMQPayload<TenantCreatedPayload>>(message);
        }
        catch (JsonException ex)
        {
            Log.Logger.Error(ex, "Failed to deserialize TenantCreatedPayload message.");
            return null;
        }
    }

    private static void HandleFailure(dynamic response)
    {
        var msg = response.result.Errors.EnumerateIdentityErrors();
        throw new InvalidOperationException(msg);
    }

    private async Task<string> CreateSuccessOutbox(
        RMQPayload<TenantCreatedPayload> payload,
        User user,
        IUnitOfWorkService uow,
        OutBoxService outboxService)
    {

        var userCreatedEvent = new RMQPayload<UserCreatedPayload>
        {
            EventId = Guid.NewGuid(),
            CausationId = payload.EventId,
            EventType = _nextEvent,
            Data = new UserCreatedPayload
            {
                TenantId = payload.Data.TenantId,
                UserId = user.Id,
                Email = payload.Data.Email,
            } 
        };

        var serializedMessage = ObjectSerializer.Serialized(userCreatedEvent);
        var outbox = outboxService.CreateModel(
            payload.Data.TenantId,
            user.Id,
            userCreatedEvent.EventId,
            userCreatedEvent.CausationId,
            userCreatedEvent.CorrelationId,
            "CreateUser",
            _nextEvent,
            OutBoxState.PROCESSING,
            serializedMessage);
        await outboxService.AddAsync(outbox);
        return serializedMessage;
    }
}