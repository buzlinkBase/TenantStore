using MassTransit;
using OnePunch.Auth.Core.Services;

namespace OnePunch.Auth.Core.Messaging;

/// <summary>
/// Flow H (Cache Invalidation): evicts the affected user's membership cache entry whenever
/// Tenant Service reports a role/membership change, rather than waiting for the 5-min TTL.
/// Redis DEL is naturally idempotent, so MassTransit's default at-least-once retry (throw on
/// failure -> redelivery -> harmless re-delete) is sufficient without an inbox/dedup table.
/// </summary>
public class MembershipChangedWorker : IConsumer<MembershipChanged>
{
    private readonly MembershipCacheService _membershipCacheService;
    private readonly UserService _userService;

    public MembershipChangedWorker(MembershipCacheService membershipCacheService,
        UserService userService)
    {
        _membershipCacheService = membershipCacheService;
        _userService = userService;
    }

    public async Task Consume(ConsumeContext<MembershipChanged> context)
    {
        if (context.Message == null) return;

        // Invalidate on every change type, matching this class's own doc comment -- the previous
        // early return here (on empty NewStatus) meant a RoleChanged event did nothing at all on
        // the AuthApi side, not even this.
        await _membershipCacheService.InvalidateAsync(context.Message.UserId);

        // One fetch, one commit for whichever of status/roles this message actually carries --
        // see ApplyMembershipChangeAsync's doc comment for why that matters (two separate
        // self-committing calls here would silently drop the second one if a message ever
        // carried both a status and a role change).
        await _userService.ApplyMembershipChangeAsync(
            context.Message.UserId,
            context.Message.TenantId,
            context.Message.NewStatus,
            context.Message.ChangeType == "RoleChanged",
            context.Message.NewRoles,
            context.CancellationToken);
    }
}
