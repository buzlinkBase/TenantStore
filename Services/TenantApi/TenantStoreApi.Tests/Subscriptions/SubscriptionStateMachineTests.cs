using FluentAssertions;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>Time-driven lifecycle transitions: each status, its access, and when it moves on.</summary>
public class SubscriptionStateMachineTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly SubscriptionStateMachine _machine = new();
    private readonly SubscriptionOptions _options = new();

    [Theory]
    [InlineData(AccountSubscriptionStatus.Trialing, SubscriptionAccessMode.Full)]
    [InlineData(AccountSubscriptionStatus.Active, SubscriptionAccessMode.Full)]
    [InlineData(AccountSubscriptionStatus.PastDue, SubscriptionAccessMode.Full)]
    [InlineData(AccountSubscriptionStatus.Suspended, SubscriptionAccessMode.ReadOnly)]
    [InlineData(AccountSubscriptionStatus.Expired, SubscriptionAccessMode.ReadOnly)]
    [InlineData(AccountSubscriptionStatus.Canceled, SubscriptionAccessMode.ReadOnly)]
    public void EachStatus_GrantsItsAccessMode(AccountSubscriptionStatus status, SubscriptionAccessMode expected)
    {
        _machine.AccessModeOf(new AccountSubscription { SubStatus = status }).Should().Be(expected);
    }

    [Fact]
    public void Trial_StaysTrialing_UntilItsEnd()
    {
        var subscription = new AccountSubscription { SubStatus = AccountSubscriptionStatus.Trialing, TrialEndsAtUtc = Now.AddMinutes(1) };

        _machine.Advance(subscription, Now, _options).Should().BeEmpty();
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Trialing);
    }

    [Fact]
    public void Trial_Expires_AtItsEnd()
    {
        var subscription = new AccountSubscription { SubStatus = AccountSubscriptionStatus.Trialing, TrialEndsAtUtc = Now };

        var transitions = _machine.Advance(subscription, Now, _options);

        transitions.Should().ContainSingle(t => t.EventType == BillingEventTypes.TrialExpired);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Expired);
    }

    [Fact]
    public void Active_UnpaidAtPeriodEnd_FallsPastDue_WithGrace()
    {
        var periodEnd = Now.AddHours(-1);
        var subscription = new AccountSubscription { SubStatus = AccountSubscriptionStatus.Active, CurrentPeriodEndUtc = periodEnd };

        _machine.Advance(subscription, Now, _options);

        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.PastDue);
        subscription.GraceEndsAtUtc.Should().Be(periodEnd.AddDays(_options.GraceDays));
    }

    [Fact]
    public void MissedRuns_CatchUp_ThroughEveryDueTransition()
    {
        // Period ended 10 days ago, grace was 7: past due AND suspended are both owed.
        var subscription = new AccountSubscription { SubStatus = AccountSubscriptionStatus.Active, CurrentPeriodEndUtc = Now.AddDays(-10) };

        var transitions = _machine.Advance(subscription, Now, _options);

        transitions.Select(t => t.To).Should().Equal(AccountSubscriptionStatus.PastDue, AccountSubscriptionStatus.Suspended);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Suspended);
    }

    [Fact]
    public void CancelAtPeriodEnd_KeepsFullAccess_ThenCancels_WithPurgeDate()
    {
        var periodEnd = Now.AddDays(3);
        var subscription = new AccountSubscription
        {
            SubStatus = AccountSubscriptionStatus.Active, CurrentPeriodEndUtc = periodEnd, CancelAtPeriodEnd = true,
        };

        _machine.Advance(subscription, Now, _options).Should().BeEmpty();
        _machine.AccessModeOf(subscription).Should().Be(SubscriptionAccessMode.Full);

        _machine.Advance(subscription, periodEnd, _options);

        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Canceled);
        subscription.CanceledAtUtc.Should().Be(periodEnd);
        subscription.PurgeAfterUtc.Should().Be(periodEnd.AddDays(_options.PurgeAfterDays));
    }

    [Fact]
    public void ScheduledDowngrade_AppliesAtItsDate_EvenAfterTheNextPeriodWasPaid()
    {
        var premium = Guid.NewGuid();
        var payroll = Guid.NewGuid();
        var requestedAgainst = Now.AddDays(-1);
        var subscription = new AccountSubscription
        {
            SubStatus = AccountSubscriptionStatus.Active,
            PlanId = premium,
            // Renewed: the period now runs a month past the date the downgrade was requested for.
            CurrentPeriodEndUtc = Now.AddMonths(1),
            PendingPlanId = payroll,
            PendingChangeTiming = PlanChangeTiming.AtPeriodEnd,
            PendingChangeEffectiveAtUtc = requestedAgainst,
            LockedMonthlyPrice = 9999m,
        };

        var transitions = _machine.Advance(subscription, Now, _options);

        transitions.Should().ContainSingle(t => t.EventType == BillingEventTypes.PlanChanged);
        subscription.PlanId.Should().Be(payroll);
        subscription.PendingPlanId.Should().BeNull();
        subscription.LockedMonthlyPrice.Should().BeNull();
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Active);
    }

    [Theory]
    [InlineData(AccountSubscriptionStatus.Suspended)]
    [InlineData(AccountSubscriptionStatus.Expired)]
    [InlineData(AccountSubscriptionStatus.Canceled)]
    public void ReadOnlyStatuses_DontMoveOnByThemselves(AccountSubscriptionStatus status)
    {
        var subscription = new AccountSubscription { SubStatus = status, GraceEndsAtUtc = Now.AddDays(-30), TrialEndsAtUtc = Now.AddDays(-30) };

        _machine.Advance(subscription, Now, _options).Should().BeEmpty();
        subscription.SubStatus.Should().Be(status);
    }
}
