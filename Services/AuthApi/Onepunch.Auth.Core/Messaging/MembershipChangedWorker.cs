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
        if (string.IsNullOrEmpty(context.Message.NewStatus)) return;
        await _membershipCacheService.InvalidateAsync(context.Message.UserId);
        await _userService.ChangedStatus(context.Message.UserId, context.Message.NewStatus ?? "", context.CancellationToken);

    }
}
