using FluentAssertions;
using MassTransit;
using Moq;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>Owner and platform commands on an account's subscription.</summary>
public class AccountSubscriptionServiceTests
{
    // ── Trial ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstCompany_StartsASevenDayPremiumTrial()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Trialing);
        subscription.TrialEndsAtUtc.Should().Be(SubscriptionTestHost.Start.AddDays(7));
        subscription.PlanId.Should().Be(host.Uow.Context.Plans.Single(p => p.Code == "premium").Id);
    }

    [Fact]
    public async Task LaterCompany_NeverStartsAnotherTrial()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();

        host.AddCompany(owner, "Second");
        var again = await host.Subscriptions.StartTrialIfFirstCompanyAsync(owner, "owner@acme.ph");

        again.Should().BeNull();
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Expired);
    }

    [Fact]
    public async Task RepeatTrial_FromTheSameBusinessDomain_IsFlagged_NotBlocked()
    {
        var host = new SubscriptionTestHost();
        await host.TrialAccountAsync("first@acme.ph");

        var (second, _) = await host.TrialAccountAsync("second@acme.ph");

        host.SubscriptionOf(second).SubStatus.Should().Be(AccountSubscriptionStatus.Trialing);
        host.Uow.Context.BillingEvents.Should().ContainSingle(e =>
            e.OwnerUserId == second && e.Type == BillingEventTypes.TrialRepeatDomainFlagged);
    }

    [Fact]
    public async Task RepeatTrial_FromAFreeEmailDomain_IsNotFlagged()
    {
        var host = new SubscriptionTestHost();
        await host.TrialAccountAsync("first@gmail.com");
        await host.TrialAccountAsync("second@gmail.com");

        host.Uow.Context.BillingEvents.Should().NotContain(e => e.Type == BillingEventTypes.TrialRepeatDomainFlagged);
    }

    [Fact]
    public async Task TrialExtension_IsAllowedOnce()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        await host.Subscriptions.ExtendTrialAsync(owner, 7, "platform");
        await host.SaveAsync();
        host.SubscriptionOf(owner).TrialEndsAtUtc.Should().Be(SubscriptionTestHost.Start.AddDays(14));

        var again = () => host.Subscriptions.ExtendTrialAsync(owner, 7, "platform");
        await again.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task TrialExtension_ReopensAnUnpaidExpiredTrial()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(9));
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();

        await host.Subscriptions.ExtendTrialAsync(owner, 5, "platform");

        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Trialing);
        subscription.TrialEndsAtUtc.Should().Be(host.Clock.UtcNow.AddDays(5));
    }

    // ── Plan changes ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChoosingAPlan_DuringTrial_WaitsForPayment()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        var quote = await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Annual, "owner");

        quote.Timing.Should().Be(PlanChangeTiming.OnPayment);
        quote.AmountDue.Should().Be(49990m);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Trialing, "nothing changes until it's paid");
        host.Publisher.Verify(p => p.Publish(It.IsAny<SubscriptionChanged>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Downgrade_WhilePaid_TakesEffectAtPeriodEnd()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        var periodEnd = host.Clock.UtcNow.AddMonths(1);
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "premium", PeriodEndUtc = periodEnd }, "platform");
        await host.SaveAsync();

        var quote = await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");

        quote.Timing.Should().Be(PlanChangeTiming.AtPeriodEnd);
        quote.EffectiveAtUtc.Should().Be(periodEnd);
        host.SubscriptionOf(owner).PlanId.Should().Be(host.Uow.Context.Plans.Single(p => p.Code == "premium").Id);
    }

    [Fact]
    public async Task Downgrade_IsBlocked_WhileOverTheTargetPlansLimits()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        host.AddCompany(owner, "Second");
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.EmployeesMax] = 150;

        var act = () => host.Subscriptions.RequestPlanChangeAsync(owner, "timekeeping", BillingCycle.Monthly, "owner");

        var error = await act.Should().ThrowAsync<PlanLimitExceededException>();
        error.Which.Violations.Select(v => v.LimitKey).Should().BeEquivalentTo(
            [EntitlementKeys.CompaniesMax, EntitlementKeys.EmployeesMax]);
    }

    [Fact]
    public async Task Downgrade_CountsEmployeesAcrossAllCompanies_NotPerCompany()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        var second = host.AddCompany(owner, "Second");
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.EmployeesMax] = 60;
        host.HrmsUsage.Usage[second][EntitlementKeys.EmployeesMax] = 60;

        // Each company is under Timekeeping's 100, together they're over.
        var timekeeping = host.Uow.Context.Plans.Single(p => p.Code == "timekeeping");
        var violations = await host.Subscriptions.FindViolationsAsync(host.SubscriptionOf(owner), timekeeping);
        violations.Should().ContainSingle(v => v.LimitKey == EntitlementKeys.EmployeesMax)
            .Which.Should().Match<LimitViolation>(v => v.TenantId == null && v.Used == 120 && v.Max == 100);
    }

    [Fact]
    public async Task Usage_IsServedApartFromBilling_SoBillingDoesntWaitOnCounting()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        host.AddCompany(owner, "Second");
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.EmployeesMax] = 7;

        var billing = await host.Views.GetBillingForTenantAsync(tenantId);
        var usage = await host.Views.GetUsageForTenantAsync(tenantId);

        billing.CompaniesUsed.Should().Be(2);
        usage.Companies.Should().HaveCount(2);
        usage.Companies.Single(c => c.TenantId == tenantId).Used[EntitlementKeys.EmployeesMax].Should().Be(7);
        usage.RecommendedPlanCode.Should().NotBeNull();
    }

    [Fact]
    public async Task EmployeeLimit_CantBeOverriddenForOneCompany()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();

        var act = () => host.Subscriptions.AddOverrideAsync(
            owner, new AddOverrideCommand { Key = EntitlementKeys.EmployeesMax, Value = "500", TenantId = tenantId }, "platform");

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task Downgrade_IsBlocked_WhenUsageCantBeCounted()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        host.HrmsUsage.Usage.Remove(tenantId);

        var act = () => host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");

        (await act.Should().ThrowAsync<PlanLimitExceededException>())
            .Which.Violations.Should().ContainSingle(v => v.LimitKey == EntitlementKeys.EmployeesMax && v.Max == null);
    }

    // ── Payments ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Payment_ActivatesTheChosenPlan_FromNow_AndLocksItsPrice()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");
        host.Clock.Advance(TimeSpan.FromDays(10));

        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999m, Method = PaymentMethod.BankTransfer }, "platform");

        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Active);
        subscription.PlanId.Should().Be(host.Uow.Context.Plans.Single(p => p.Code == "payroll").Id);
        subscription.CurrentPeriodStartUtc.Should().Be(host.Clock.UtcNow);
        subscription.CurrentPeriodEndUtc.Should().Be(host.Clock.UtcNow.AddMonths(1));
        subscription.LockedMonthlyPrice.Should().Be(4999m);
        subscription.PendingPlanId.Should().BeNull();
    }

    [Fact]
    public async Task Payment_BeforePeriodEnd_RenewsFromThePeriodEnd()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        var periodEnd = host.Clock.UtcNow.AddMonths(1);
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = periodEnd }, "platform");
        await host.SaveAsync();

        var payment = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999m, Method = PaymentMethod.GCash }, "platform");

        payment.PeriodStartUtc.Should().Be(periodEnd);
        host.SubscriptionOf(owner).CurrentPeriodEndUtc.Should().Be(periodEnd.AddMonths(1));
    }

    [Fact]
    public async Task Payment_ReactivatesASuspendedAccount()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus = AccountSubscriptionStatus.Suspended;
        subscription.GraceEndsAtUtc = host.Clock.UtcNow.AddDays(-1);
        await host.SaveAsync();

        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 9999m, Method = PaymentMethod.Cash }, "platform");
        await host.SaveAsync();

        (await host.Resolver.ResolveForTenantAsync(tenantId)).IsReadOnly.Should().BeFalse();
        host.SubscriptionOf(owner).GraceEndsAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ExistingSubscribers_KeepTheirLockedPrice_AfterAPriceRise()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        host.Uow.Context.Plans.Single(p => p.Code == "payroll").MonthlyPrice = 6000m;
        await host.SaveAsync();

        var subscription = host.SubscriptionOf(owner);
        var price = await host.Subscriptions.PriceForAsync(subscription, subscription.Plan, BillingCycle.Monthly);

        price.Should().Be(4999m);
    }

    [Fact]
    public async Task Discount_ReducesThePrice()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.AddDiscountAsync(owner, new AddDiscountCommand { Percent = 20 }, "platform");
        await host.SaveAsync();

        var subscription = host.SubscriptionOf(owner);
        var payroll = host.Uow.Context.Plans.Single(p => p.Code == "payroll");

        (await host.Subscriptions.PriceForAsync(subscription, payroll, BillingCycle.Monthly)).Should().Be(3999.20m);
    }

    // ── Cancel / resume ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_ThenResume_WhileActive()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");

        await host.Subscriptions.CancelAsync(owner, "owner");
        host.SubscriptionOf(owner).CancelAtPeriodEnd.Should().BeTrue();

        await host.Subscriptions.ResumeAsync(owner, "owner");
        host.SubscriptionOf(owner).CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public async Task Cancel_DuringTrial_IsRefused()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        var act = () => host.Subscriptions.CancelAsync(owner, "owner");

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }
}
