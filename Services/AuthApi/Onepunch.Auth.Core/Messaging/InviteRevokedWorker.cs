using MassTransit;
using Onepunch.Auth.Core.Services;

namespace OnePunch.Auth.Core.Messaging;

/// <summary>
/// Tenant Service reports an admin revoked/removed a pending invite: expire the invitation link.
/// Idempotent -- expiring an already expired or accepted invitation is a no-op, so MassTransit's
/// at-least-once redelivery is harmless.
/// </summary>
public class InviteRevokedWorker : IConsumer<InviteRevoked>
{
    private readonly InvitationService _invitationService;

    public InviteRevokedWorker(InvitationService invitationService)
    {
        _invitationService = invitationService;
    }

    public async Task Consume(ConsumeContext<InviteRevoked> context)
    {
        var message = context.Message;
        if (message == null || string.IsNullOrWhiteSpace(message.Email)) return;
        await _invitationService.ExpirePendingAsync(message.TenantId, message.Email, context.CancellationToken);
    }
}
