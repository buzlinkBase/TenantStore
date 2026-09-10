using MassTransit;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

/// <summary>
/// Flow B: consumes the UserInvited event published when an owner/admin sends an invite,
/// creating a pending membership placeholder before the invitee ever accepts.
/// </summary>
public class UserInvitedWorker : IConsumer<UserInvited>
{
    private readonly UserMembershipService _userMembershipService;

    public UserInvitedWorker(UserMembershipService userMembershipService)
    {
        _userMembershipService = userMembershipService;
    }

    public async Task Consume(ConsumeContext<UserInvited> context)
    {
        var message = context.Message;
        await _userMembershipService.CreateInvitePlaceholderAsync(
            message.TenantId,
            message.TenantName,
            message.Email,
            message.Roles.Count > 0 ? message.Roles : ["Member"],
            context.CancellationToken);
        await _userMembershipService.CommitChangesAsync(context.CancellationToken);
    }
}
