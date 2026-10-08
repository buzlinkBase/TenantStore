using MassTransit;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core.Messaging;

/// <summary>
/// A resigned/terminated/retired/deceased employee loses access to the company: their membership
/// is Revoked, which MembershipChanged then turns into a forced logout (AuthApi). Idempotent --
/// a redelivery finds the membership already Revoked and does nothing.
/// </summary>
public class EmployeeSeparatedWorker : IConsumer<EmployeeSeparated>
{
    private readonly UserMembershipService _userMembershipService;

    public EmployeeSeparatedWorker(UserMembershipService userMembershipService)
    {
        _userMembershipService = userMembershipService;
    }

    public async Task Consume(ConsumeContext<EmployeeSeparated> context)
    {
        var message = context.Message;
        var revoked = await _userMembershipService.RevokeForSeparatedEmployeeAsync(
            message.UserId, message.TenantId, context.CancellationToken);
        if (revoked) await _userMembershipService.CommitChangesAsync(context.CancellationToken);
    }
}
