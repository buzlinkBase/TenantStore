using FluentAssertions;
using Onepunch.Common.Lib.Subscriptions;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>
/// Services cache a company's entitlements for a few minutes. A subscription turns read-only by the
/// calendar (trial end, grace end) with no event to evict the cache, so a full-access snapshot must
/// never be cached past that date -- otherwise a lapsed company keeps saving until the cache expires.
/// </summary>
public class EntitlementCacheTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private static EntitlementSnapshot Full(
        DateTime? trialEnds = null, DateTime? periodEnds = null, DateTime? graceEnds = null) => new()
    {
        AccessMode = SubscriptionAccessMode.Full,
        TrialEndsAtUtc = trialEnds,
        CurrentPeriodEndUtc = periodEnds,
        GraceEndsAtUtc = graceEnds,
    };

    [Fact]
    public void FullAccess_WithNothingAboutToLapse_UsesTheUsualFiveMinutes() =>
        CachedEntitlementProvider.ExpiryFor(Full(periodEnds: Now.AddDays(20)), Now).Should().Be(FiveMinutes);

    [Fact]
    public void FullAccess_TrialEndingSoon_IsNotCachedPastTheTrialEnd()
    {
        var expiry = CachedEntitlementProvider.ExpiryFor(Full(trialEnds: Now.AddSeconds(40)), Now);

        expiry.Should().BeGreaterThan(TimeSpan.FromSeconds(40), "the next read must come after the lock");
        expiry.Should().BeLessThan(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void FullAccess_PastDue_IsNotCachedPastTheGraceEnd()
    {
        var expiry = CachedEntitlementProvider.ExpiryFor(Full(periodEnds: Now.AddDays(-3), graceEnds: Now.AddMinutes(2)), Now);

        expiry.Should().BeGreaterThan(TimeSpan.FromMinutes(2)).And.BeLessThan(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void FullAccess_TheNearestDateWins()
    {
        var expiry = CachedEntitlementProvider.ExpiryFor(
            Full(trialEnds: Now.AddHours(2), periodEnds: Now.AddSeconds(30), graceEnds: Now.AddDays(7)), Now);

        expiry.Should().BeLessThan(TimeSpan.FromSeconds(35));
    }

    [Fact]
    public void DatesAlreadyPassed_AreIgnored() =>
        CachedEntitlementProvider.ExpiryFor(Full(trialEnds: Now.AddDays(-1)), Now).Should().Be(FiveMinutes);

    [Fact]
    public void ReadOnly_UsesTheUsualFiveMinutes() =>
        CachedEntitlementProvider.ExpiryFor(
            new EntitlementSnapshot { AccessMode = SubscriptionAccessMode.ReadOnly, GraceEndsAtUtc = Now.AddSeconds(10) }, Now)
            .Should().Be(FiveMinutes);
}
