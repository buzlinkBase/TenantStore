using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>Operator actions on an account: suspend, reinstate, cancel, extend, void a payment.</summary>
public class PlatformAccountActionsServiceTests
{
    private const string Operator = "platform:ops@onepunch.test";

    private static PlatformAccountActionsService Actions(SubscriptionTestHost host) => new(
        host.Uow, host.Subscriptions, host.Events, host.Notifier, host.StateMachine,
        Microsoft.Extensions.Options.Options.Create(host.Options), host.Clock);

    /// <summary>An Active account on Payroll, paid up for a month.</summary>
    private static async Task<Guid> ActiveAccountAsync(SubscriptionTestHost host)
    {
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        return owner;
    }

    private static List<BillingEvent> EventsOf(SubscriptionTestHost host, Guid owner, string type) =>
        host.Uow.Context.BillingEvents.Where(e => e.OwnerUserId == owner && e.Type == type).ToList();

    // ── Suspend ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Suspend_MakesTheAccountReadOnly_AndRecordsWhoAndWhy()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);

        await Actions(host).SuspendAsync(owner, "  Chargeback dispute ", Operator);
        await host.SaveAsync();

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Suspended);
        var recorded = EventsOf(host, owner, BillingEventTypes.SuspendedByAdmin).Should().ContainSingle().Subject;
        recorded.Actor.Should().Be(Operator);
        recorded.PayloadJson.Should().Contain("Chargeback dispute");
        host.Publisher.Verify(p => p.Publish(It.Is<Onepunch.Common.Lib.DTO.SubscriptionChanged>(m => m.OwnerUserId == owner), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public async Task EveryAction_NeedsARealReason(string? reason)
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);
        var actions = Actions(host);

        await Assert.ThrowsAsync<SubscriptionRuleException>(() => actions.SuspendAsync(owner, reason, Operator));
        await Assert.ThrowsAsync<SubscriptionRuleException>(() => actions.ReinstateAsync(owner, reason, Operator));
        await Assert.ThrowsAsync<SubscriptionRuleException>(() => actions.CancelNowAsync(owner, reason, Operator));
        await Assert.ThrowsAsync<SubscriptionRuleException>(() => actions.ExtendPeriodAsync(owner, 5, reason, Operator));
        await Assert.ThrowsAsync<SubscriptionRuleException>(() => actions.VoidPaymentAsync(Guid.NewGuid(), reason, Operator));
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active, "a refused action changes nothing");
    }

    [Fact]
    public async Task Suspend_RefusesAnAccountThatIsAlreadyLapsed()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();

        await Assert.ThrowsAsync<SubscriptionRuleException>(() => Actions(host).SuspendAsync(owner, "because", Operator));
    }

    [Fact]
    public async Task AnAccountWithoutASubscription_IsNotFound()
    {
        var act = () => Actions(new SubscriptionTestHost()).SuspendAsync(Guid.NewGuid(), "because", Operator);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Reinstate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reinstate_AfterASuspension_GivesBackActiveAccessWhilePaidUp()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);
        await Actions(host).SuspendAsync(owner, "dispute", Operator);
        await host.SaveAsync();

        await Actions(host).ReinstateAsync(owner, "dispute settled", Operator);
        await host.SaveAsync();

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active);
        EventsOf(host, owner, BillingEventTypes.Reinstated).Should().ContainSingle().Which.Actor.Should().Be(Operator);
    }

    [Fact]
    public async Task Reinstate_WhenThePaidPeriodHasEnded_ChangesNothingAndSaysWhatToDoInstead()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);
        await Actions(host).SuspendAsync(owner, "dispute", Operator);
        await host.SaveAsync();
        host.Clock.Advance(TimeSpan.FromDays(45));

        var act = () => Actions(host).ReinstateAsync(owner, "retry", Operator);

        (await act.Should().ThrowAsync<SubscriptionRuleException>()).Which.Message.Should().Contain("Record a payment");
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Suspended);
    }

    [Fact]
    public async Task Reinstate_ATrialThatIsStillRunning_GoesBackToTrialing()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await Actions(host).SuspendAsync(owner, "abuse report", Operator);
        await host.SaveAsync();

        await Actions(host).ReinstateAsync(owner, "cleared", Operator);

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Trialing);
    }

    [Fact]
    public async Task Reinstate_AnActiveAccount_HasNothingToDo()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);

        await Assert.ThrowsAsync<SubscriptionRuleException>(() => Actions(host).ReinstateAsync(owner, "why not", Operator));
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelNow_EndsTheSubscriptionAtOnce_AndSchedulesThePurge()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);

        await Actions(host).CancelNowAsync(owner, "Closed the business", Operator);
        await host.SaveAsync();

        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Canceled);
        subscription.CanceledAtUtc.Should().Be(host.Clock.UtcNow);
        subscription.PurgeAfterUtc.Should().Be(host.Clock.UtcNow.AddDays(host.Options.PurgeAfterDays));
        EventsOf(host, owner, BillingEventTypes.CanceledByAdmin).Should().ContainSingle();
    }

    [Fact]
    public async Task CancelNow_TwiceIsRefused()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);
        await Actions(host).CancelNowAsync(owner, "closing", Operator);
        await host.SaveAsync();

        await Assert.ThrowsAsync<SubscriptionRuleException>(() => Actions(host).CancelNowAsync(owner, "closing again", Operator));
    }

    // ── Extend ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtendPeriod_AddsDaysToThePaidPeriod()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);
        var before = host.SubscriptionOf(owner).CurrentPeriodEndUtc!.Value;

        await Actions(host).ExtendPeriodAsync(owner, 14, "Outage compensation", Operator);
        await host.SaveAsync();

        host.SubscriptionOf(owner).CurrentPeriodEndUtc.Should().Be(before.AddDays(14));
        EventsOf(host, owner, BillingEventTypes.PeriodExtended).Should().ContainSingle().Which.PayloadJson.Should().Contain("Outage");
    }

    [Fact]
    public async Task ExtendPeriod_ARecentlyLapsedAccount_ComesBackToActive()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);
        host.Clock.Advance(TimeSpan.FromDays(35)); // 4 days past the period end
        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.PastDue);

        await Actions(host).ExtendPeriodAsync(owner, 30, "Bank delay", Operator);
        await host.SaveAsync();

        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Active);
        subscription.GraceEndsAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ExtendPeriod_MovesAScheduledDowngradeWithIt()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "premium", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");
        await host.SaveAsync();
        var effective = host.SubscriptionOf(owner).PendingChangeEffectiveAtUtc!.Value;

        await Actions(host).ExtendPeriodAsync(owner, 10, "goodwill", Operator);
        await host.SaveAsync();

        host.SubscriptionOf(owner).PendingChangeEffectiveAtUtc.Should().Be(effective.AddDays(10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(366)]
    public async Task ExtendPeriod_RefusesAnOutOfRangeNumberOfDays(int days)
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);

        await Assert.ThrowsAsync<SubscriptionRuleException>(() => Actions(host).ExtendPeriodAsync(owner, days, "reason", Operator));
    }

    [Fact]
    public async Task ExtendPeriod_IsNotForTrials()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        var act = () => Actions(host).ExtendPeriodAsync(owner, 7, "more time", Operator);

        (await act.Should().ThrowAsync<SubscriptionRuleException>()).Which.Message.Should().Contain("trial extension");
    }

    // ── Void a payment ───────────────────────────────────────────────────────

    private static async Task<(Guid Owner, SubscriptionPayment First, SubscriptionPayment Renewal)> TwoPaymentsAsync(SubscriptionTestHost host)
    {
        var (owner, _) = await host.TrialAccountAsync();
        var first = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999, Method = PaymentMethod.BankTransfer, PlanCode = "payroll" }, "platform");
        await host.SaveAsync();
        host.Clock.Advance(TimeSpan.FromDays(10));
        var renewal = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999, Method = PaymentMethod.BankTransfer }, "platform");
        await host.SaveAsync();
        return (owner, first, renewal);
    }

    [Fact]
    public async Task VoidingTheLatestPayment_RollsThePaidPeriodBackToThePreviousOne()
    {
        var host = new SubscriptionTestHost();
        var (owner, first, renewal) = await TwoPaymentsAsync(host);
        host.SubscriptionOf(owner).CurrentPeriodEndUtc.Should().Be(renewal.PeriodEndUtc);

        await Actions(host).VoidPaymentAsync(renewal.Id, "Wrong amount keyed in", Operator);
        await host.SaveAsync();

        var subscription = host.SubscriptionOf(owner);
        subscription.CurrentPeriodEndUtc.Should().Be(first.PeriodEndUtc);
        subscription.CurrentPeriodStartUtc.Should().Be(first.PeriodStartUtc);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Active);

        var voided = host.Uow.Context.SubscriptionPayments.Single(p => p.Id == renewal.Id);
        voided.VoidedAtUtc.Should().Be(host.Clock.UtcNow);
        voided.VoidedBy.Should().Be(Operator);
        voided.VoidReason.Should().Be("Wrong amount keyed in");
        host.Uow.Context.SubscriptionPayments.Count().Should().Be(2, "a void never deletes the money trail");
    }

    [Fact]
    public async Task VoidingAnOlderPayment_LeavesTheCurrentPeriodAlone()
    {
        var host = new SubscriptionTestHost();
        var (owner, first, renewal) = await TwoPaymentsAsync(host);

        await Actions(host).VoidPaymentAsync(first.Id, "Duplicate entry", Operator);
        await host.SaveAsync();

        host.SubscriptionOf(owner).CurrentPeriodEndUtc.Should().Be(renewal.PeriodEndUtc);
    }

    [Fact]
    public async Task VoidingTheOnlyPayment_FallsBackToTheTrial_WhileItIsStillRunning()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        var payment = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999, Method = PaymentMethod.Cash, PlanCode = "payroll" }, "platform");
        await host.SaveAsync();

        await Actions(host).VoidPaymentAsync(payment.Id, "Never received", Operator);
        await host.SaveAsync();

        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Trialing);
        subscription.CurrentPeriodEndUtc.Should().BeNull();
    }

    [Fact]
    public async Task VoidingTheOnlyPayment_AfterTheTrialEnded_LeavesTheAccountExpired()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(10));
        var payment = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999, Method = PaymentMethod.Cash, PlanCode = "payroll" }, "platform");
        await host.SaveAsync();

        await Actions(host).VoidPaymentAsync(payment.Id, "Bounced check", Operator);
        await host.SaveAsync();

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Expired);
    }

    [Fact]
    public async Task VoidingAPayment_WhosePreviousPeriodHasAlreadyEnded_AppliesTheDueTransition()
    {
        var host = new SubscriptionTestHost();
        var (owner, first, renewal) = await TwoPaymentsAsync(host);
        host.Clock.Advance(TimeSpan.FromDays(25)); // past the first period's end, inside the renewal's
        host.SubscriptionOf(owner).CurrentPeriodEndUtc.Should().Be(renewal.PeriodEndUtc);
        host.Clock.UtcNow.Should().BeAfter(first.PeriodEndUtc);

        await Actions(host).VoidPaymentAsync(renewal.Id, "Reversed by the bank", Operator);
        await host.SaveAsync();

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.PastDue);
        EventsOf(host, owner, BillingEventTypes.PastDue).Should().ContainSingle().Which.Actor.Should().Be(Operator);
    }

    [Fact]
    public async Task AVoidedPayment_CannotBeVoidedAgain_AndAnUnknownOneIsNotFound()
    {
        var host = new SubscriptionTestHost();
        var (_, first, _) = await TwoPaymentsAsync(host);
        await Actions(host).VoidPaymentAsync(first.Id, "Duplicate", Operator);
        await host.SaveAsync();

        await Assert.ThrowsAsync<SubscriptionRuleException>(() => Actions(host).VoidPaymentAsync(first.Id, "Again", Operator));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Actions(host).VoidPaymentAsync(Guid.NewGuid(), "Nothing", Operator));
    }

    [Fact]
    public async Task VoidedPayments_AreHiddenFromTheOwnersBillingHistory_ButKeptForThePlatform()
    {
        var host = new SubscriptionTestHost();
        var (owner, first, _) = await TwoPaymentsAsync(host);
        await Actions(host).VoidPaymentAsync(first.Id, "Duplicate", Operator);
        await host.SaveAsync();
        var subscription = host.SubscriptionOf(owner);

        (await host.Subscriptions.GetPaymentsAsync(subscription.Id)).Should().NotContain(p => p.Id == first.Id);
        (await host.Subscriptions.GetPaymentsAsync(subscription.Id, includeVoided: true)).Should().Contain(p => p.Id == first.Id);
        (await host.Views.GetBillingForAccountAsync(owner)).Payments.Should().NotContain(p => p.Id == first.Id);
    }
}
