using Microsoft.Extensions.DependencyInjection;
using Onepunch.Auth.Core.Providers;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core.Services;
using System.Security.Cryptography;

namespace OnePunch.Auth.Core.Messaging;

public class TenantForConfirmationHandler : IMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMQPublisher _publisher;
    private readonly IAuthDomainProvider _domainProvider;
    private const string _nextEvent = "notif.tenant.for.confirmation";
    public TenantForConfirmationHandler(IServiceScopeFactory scopeFactory,
        IRabbitMQPublisher publisher,
        IAuthDomainProvider domainProvider)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _domainProvider = domainProvider;
    }

    public async Task Handle(string message)
    {
        var model = ObjectSerializer.DeSerialized<MessagePayload<TenantForConfirmation>>(message);
        if (model == null) return;
        using var scope = _scopeFactory.CreateScope();

        var Uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkService>();
        var userService = scope.ServiceProvider.GetRequiredService<UserService>();
        var outboxService = scope.ServiceProvider.GetRequiredService<OutBoxService>();

        if (await outboxService.IsEventExists(model.EventId)) return;

        //user
        var user = await userService.GetByIdAsync(model.Data.UserId.ToString());
        if (user == null)
        {
            //TODO _publisher.PublishAsync("fail pub")
            throw new Exception($"unable to find user {model.Data.Email}");
        }
        user.Status = "For Confirmation";
        var userUpdateResult = await userService.UpdateAsync(user);
        if (!userUpdateResult.Succeeded)
        {
            var err = userUpdateResult.Errors.EnumerateIdentityErrors();
            throw new Exception(err);
        }

        //set processed
        var priorOutbox = await outboxService.FindEvent(model.CausationId);
        if (priorOutbox != null)
        {
            await outboxService.UpdateStateAsync(priorOutbox, OutBoxState.PROCESSED);
        }

        //current
        var token = GenerateEmailToken(model.Data);
        StoreToken(Uow, model, token);
        var messPayload = ComposePayload(model, token);
        var serializedMessage = ObjectSerializer.Serialized(messPayload);

        var current = outboxService.CreateModel(
            model.Data.TenantId,
            user.Id,
            messPayload.EventId,
            messPayload.CausationId,
            messPayload.CorrelationId,
            "ForConfirmationTenant",
            _nextEvent,
            OutBoxState.PROCESSING,
            serializedMessage);

        await outboxService.AddAsync(current);
        await Uow.CommitChangesAsync();

        await _publisher.PublishAsync(serializedMessage, _nextEvent);

    }

    private void StoreToken(IUnitOfWorkService uow, MessagePayload<TenantForConfirmation> payload, string token)
    {
        var tokenModel = new EmailToken
        {
            EventId = payload.EventId,
            CausationId = payload.CausationId,
            CorrelationId = payload.CorrelationId,
            EventType = payload.EventType,
            TenantId = payload.Data.TenantId,
            UserId = payload.Data.UserId,
            Email = payload.Data.Email,
            Expiry=DateTime.UtcNow.AddDays(2),
            TokenType="TenantConfirmation",
            TokenValue=token,
        };
        uow.Context.EmailTokens.Add(tokenModel);
    }

    private string GenerateEmailToken(TenantForConfirmation model) => TokenGenerator.Generate(model.TenantId, model.Email); 

    private MessagePayload<NotifTenantForConfirmation> ComposePayload(MessagePayload<TenantForConfirmation> model, string token)
    {
        return new MessagePayload<NotifTenantForConfirmation>
        {
            EventId = Guid.NewGuid(),
            CausationId = model.EventId,
            EventType = _nextEvent,
            Data = new NotifTenantForConfirmation
            {
                Token = token,
                Email = model.Data.Email,
                TenantId = model.Data.TenantId,
                UserId = model.Data.UserId,
                Expiry = DateTime.UtcNow.AddDays(2),
                IssuedAt = DateTime.UtcNow,
                Purpose = "Tenant account confirmation",
                ConfirmationRoute = string.Concat( _domainProvider.Resolve().Apis.Bio,"/api/v1/user/confirm-email")
            }
        };
    }
}
