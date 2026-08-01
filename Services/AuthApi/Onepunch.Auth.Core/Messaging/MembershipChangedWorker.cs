using MassTransit;
using Onepunch.Auth.Core.Services;
using Onepunch.Common.Lib.DTO;

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

    public MembershipChangedWorker(MembershipCacheService membershipCacheService)
    {
        _membershipCacheService = membershipCacheService;
    }

    public async Task Consume(ConsumeContext<MembershipChanged> context)
    {
        await _membershipCacheService.InvalidateAsync(context.Message.UserId);
    }
}
