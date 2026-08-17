using MassTransit;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

public class InvitationAcceptedWorker : IConsumer<InvitationAccepted>
{
    private readonly UserMembershipService _membershipService;

    public InvitationAcceptedWorker(UserMembershipService membershipService)
    {
        _membershipService = membershipService;
    }

    public async Task Consume(ConsumeContext<InvitationAccepted> context)
    {
        var msg = context.Message;

        var placeholder = await _membershipService.FindPendingInviteAsync(
            msg.TenantId, msg.Email, context.CancellationToken);

        if (placeholder == null) return;

        await _membershipService.ActivateInviteAsync(placeholder, msg.UserId, context.CancellationToken);
        await _membershipService.CommitChangesAsync(context.CancellationToken);

    }
}
