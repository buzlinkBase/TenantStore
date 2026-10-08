using FluentAssertions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>What an account can pay for right now -- what Pay now on the billing screen charges.</summary>
public class PaymentDueTests
{
    /// <summary>An account paid up on <paramref name="plan"/> (monthly) for one month from now.</summary>
    private static async Task<(SubscriptionTestHost Host, Guid Owner, DateTime PeriodEnd)> PaidAccountAsync(string plan = "premium")
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        var periodEnd = host.Clock.UtcNow.AddMonths(1);
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = plan, PeriodEndUtc = periodEnd }, "platform");
        await host.SaveAsync();
        return (host, owner, periodEnd);
    }

    private static Task<PaymentDue?> DueAsync(SubscriptionTestHost host, Guid owner) =>
        host.Subscriptions.GetPaymentDueAsync(host.SubscriptionOf(owner));

    [Fact]
    public async Task Trial_WithNoPlanChosen_HasNothingDue()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        (await DueAsync(host, owner)).Should().BeNull();
    }

    [Fact]
    public async Task EndedTrial_WithNoPlanChosen_HasNothingDue()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();

        (await DueAsync(host, owner)).Should().BeNull("an unpaid trial chooses a plan first");
    }

    [Fact]
    public async Task ChosenPlan_IsDue_AtItsPriceAndCycle()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Annual, "owner");

        var due = await DueAsync(host, owner);

        due!.Plan.Code.Should().Be("payroll");
        due.BillingCycle.Should().Be(BillingCycle.Annual);
        due.Amount.Should().Be(49990m);
        due.Reason.Should().Be(PaymentDueReason.PlanChange);
    }

    [Fact]
    public async Task PaidPeriod_IsDueForRenewal_OnlyOnceTheRenewalWindowOpens()
    {
        var (host, owner, periodEnd) = await PaidAccountAsync();

        (await DueAsync(host, owner)).Should().BeNull();

        host.Clock.UtcNow = periodEnd.AddDays(-host.Options.RenewalWindowDays);
        var due = await DueAsync(host, owner);

        due!.Plan.Code.Should().Be("premium");
        due.Amount.Should().Be(9999m);
        due.Reason.Should().Be(PaymentDueReason.Renewal);
    }

    [Fact]
    public async Task CanceledAtPeriodEnd_IsNotOfferedARenewal()
    {
        var (host, owner, periodEnd) = await PaidAccountAsync();
        await host.Subscriptions.CancelAsync(owner, "owner");
        host.Clock.UtcNow = periodEnd.AddDays(-1);

        (await DueAsync(host, owner)).Should().BeNull();
    }

    [Fact]
    public async Task ScheduledDowngrade_IsRenewedOnTheNewPlan_AtItsPrice()
    {
        var (host, owner, periodEnd) = await PaidAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");
        host.Clock.UtcNow = periodEnd.AddDays(-2);

        var due = await DueAsync(host, owner);

        due!.Plan.Code.Should().Be("payroll");
        due.Amount.Should().Be(4999m);
        due.Reason.Should().Be(PaymentDueReason.Renewal);
    }

    [Fact]
    public async Task PastDue_IsDueForRenewal()
    {
        var (host, owner, _) = await PaidAccountAsync("payroll");
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.PastDue;

        var due = await DueAsync(host, owner);

        due!.Plan.Code.Should().Be("payroll");
        due.Reason.Should().Be(PaymentDueReason.Renewal);
    }

    [Theory]
    [InlineData(AccountSubscriptionStatus.Suspended)]
    [InlineData(AccountSubscriptionStatus.Canceled)]
    [InlineData(AccountSubscriptionStatus.Expired)]
    public async Task LapsedPaidSubscription_IsDueForReactivation(AccountSubscriptionStatus status)
    {
        var (host, owner, _) = await PaidAccountAsync("payroll");
        host.SubscriptionOf(owner).SubStatus = status;

        var due = await DueAsync(host, owner);

        due!.Plan.Code.Should().Be("payroll");
        due.Reason.Should().Be(PaymentDueReason.Reactivation);
    }

    [Fact]
    public async Task BillingView_ShowsWhatsDue_AndWhetherOnlinePaymentIsOn()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");
        await host.SaveAsync();

        var view = await host.Views.GetBillingForTenantAsync(tenantId);

        view.OnlinePaymentEnabled.Should().BeTrue();
        view.PaymentDue.Should().Be(new PaymentDueView("payroll", "Payroll", BillingCycle.Monthly, 4999m, PaymentDueReason.PlanChange));

        host.Gateway.IsEnabled = false;
        (await host.Views.GetBillingForTenantAsync(tenantId)).OnlinePaymentEnabled.Should().BeFalse();
    }

    // ── Paying ahead for a scheduled downgrade ───────────────────────────────────────────────

    [Fact]
    public async Task PayingForAScheduledDowngrade_PaysTheNextPeriod_AndTheDowngradeStillHappensAtPeriodEnd()
    {
        var (host, owner, periodEnd) = await PaidAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");
        host.Clock.UtcNow = periodEnd.AddDays(-2);

        var payment = await host.Subscriptions.RecordPaymentAsync(owner,
            new RecordPaymentCommand { Amount = 4999m, Method = PaymentMethod.GCash, PlanCode = "payroll", BillingCycle = BillingCycle.Monthly }, "paymongo");
        await host.SaveAsync();

        payment.PeriodStartUtc.Should().Be(periodEnd);
        payment.PeriodEndUtc.Should().Be(periodEnd.AddMonths(1));
        var subscription = host.SubscriptionOf(owner);
        subscription.PlanId.Should().Be(PlanIdOf(host, "premium"), "Premium runs to the end of what was paid for it");
        subscription.CurrentPeriodEndUtc.Should().Be(periodEnd.AddMonths(1));
        subscription.PendingChangeTiming.Should().Be(PlanChangeTiming.AtPeriodEnd);

        var transitions = host.StateMachine.Advance(subscription, periodEnd.AddHours(1), host.Options);

        transitions.Should().ContainSingle(t => t.EventType == BillingEventTypes.PlanChanged);
        subscription.PlanId.Should().Be(PlanIdOf(host, "payroll"));
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Active, "the new plan's period is paid");
        subscription.CurrentPeriodEndUtc.Should().Be(periodEnd.AddMonths(1));
    }

    private static Guid PlanIdOf(SubscriptionTestHost host, string code) =>
        host.Uow.Context.Plans.Single(p => p.Code == code).Id;
}
