using FluentAssertions;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Infrastructure.EntityConfigs;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>What the seeded plans grant out of the box.</summary>
public class PlanSeedTests
{
    private static string? ValueOf(Guid planId, string key) =>
        PlanSeed.Entitlements().SingleOrDefault(e => e.PlanId == planId && e.Key == key)?.Value;

    [Theory]
    [InlineData(EntitlementKeys.TimekeepingMobile)]
    [InlineData(EntitlementKeys.PatrolApp)]
    public void TheMobileAndPatrolApps_AreIncludedInPremiumOnly(string key)
    {
        ValueOf(PlanSeed.PremiumId, key).Should().Be("true");
        ValueOf(PlanSeed.PayrollId, key).Should().Be("false");
        ValueOf(PlanSeed.TimekeepingId, key).Should().Be("false");
    }

    [Theory]
    [InlineData(EntitlementKeys.ProcurementCore)]
    [InlineData(EntitlementKeys.AccountingCore)]
    [InlineData(EntitlementKeys.BillingCore)]
    [InlineData(EntitlementKeys.ClientPortalCore)]
    public void TheSeparateSystems_AreOffInEveryStandardPlan(string key)
    {
        foreach (var plan in new[] { PlanSeed.TimekeepingId, PlanSeed.PayrollId, PlanSeed.PremiumId })
            ValueOf(plan, key).Should().Be("false", $"{key} is sold on top of a plan, not inside one");
    }

    [Fact]
    public void EveryFeature_HasASeededValueForEachPlan()
    {
        foreach (var key in EntitlementKeys.Features)
        foreach (var plan in new[] { PlanSeed.TimekeepingId, PlanSeed.PayrollId, PlanSeed.PremiumId })
            ValueOf(plan, key).Should().NotBeNull($"{key} on {plan}");
    }
}
