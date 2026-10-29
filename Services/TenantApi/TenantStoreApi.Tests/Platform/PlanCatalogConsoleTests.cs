using FluentAssertions;
using Moq;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Security;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>The console's plan catalog operations: create, bulk entitlements, impact.</summary>
public class PlanCatalogConsoleTests
{
    private const string Operator = "platform:ops@onepunch.test";

    private static PlanCatalogService Catalog(SubscriptionTestHost host) => new(host.Uow, host.Events, host.Notifier);

    private static Dictionary<string, string> ValuesOf(SubscriptionTestHost host, string code) =>
        host.Uow.Context.Plans.Single(p => p.Code == code).Entitlements.ToDictionary(e => e.Key, e => e.Value);

    // ── Catalog ──────────────────────────────────────────────────────────────

    [Fact]
    public void EveryEntitlementKey_HasACatalogEntry_OfTheRightKind()
    {
        foreach (var key in EntitlementKeys.Features)
            EntitlementCatalog.Find(key)!.Kind.Should().Be(EntitlementKind.Feature, key);
        foreach (var key in EntitlementKeys.Limits)
            EntitlementCatalog.Find(key)!.Kind.Should().Be(EntitlementKind.Limit, key);
        EntitlementCatalog.Find(EntitlementKeys.SupportTier)!.Kind.Should().Be(EntitlementKind.Display);
        EntitlementCatalog.All.Select(e => e.Key).Should().OnlyHaveUniqueItems();
        EntitlementCatalog.All.Should().OnlyContain(e => EntitlementKeys.IsKnown(e.Key), "the catalog must not invent keys");
    }

    [Fact]
    public void EveryEntitlement_IsGroupedUnderTheSystemItBelongsTo()
    {
        foreach (var key in EntitlementKeys.SeparateSystems)
            EntitlementCatalog.Find(key)!.Product.Should().NotBe(PermissionProducts.Hrms, $"{key} is its own system");
        EntitlementCatalog.Find(EntitlementKeys.ProcurementCore)!.Product.Should().Be(PermissionProducts.Procurement);
        EntitlementCatalog.Find(EntitlementKeys.AccountingCore)!.Product.Should().Be(PermissionProducts.Accounting);
        EntitlementCatalog.Find(EntitlementKeys.BillingCore)!.Product.Should().Be(PermissionProducts.Billing);
        EntitlementCatalog.Find(EntitlementKeys.ClientPortalCore)!.Product.Should().Be(PermissionProducts.ClientPortal);

        EntitlementCatalog.Find(EntitlementKeys.PayrollCore)!.Product.Should().Be(PermissionProducts.Hrms);
        // Every limit and the support tier are plan-wide, not part of one system.
        foreach (var key in EntitlementKeys.Limits.Append(EntitlementKeys.SupportTier))
            EntitlementCatalog.Find(key)!.Product.Should().Be(EntitlementCatalog.AccountWide, key);
        EntitlementCatalog.All.Where(e => e.Kind == EntitlementKind.Feature)
            .Should().OnlyContain(e => e.Product != EntitlementCatalog.AccountWide, "features belong to a system");
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_MakesAHiddenPlan_WithEverythingOffAndLimitsAtZero()
    {
        var host = new SubscriptionTestHost();

        var plan = await Catalog(host).CreateAsync(new CreatePlanCommand { Code = "Starter", Name = " Starter ", MonthlyPrice = 499, AnnualPrice = 4990 }, Operator);
        await host.SaveAsync();

        plan.Code.Should().Be("starter");
        plan.Name.Should().Be("Starter");
        plan.IsPublic.Should().BeFalse("a new plan is hidden until it is set up");
        plan.IsRecommended.Should().BeFalse();
        var values = ValuesOf(host, "starter");
        EntitlementKeys.Features.Should().OnlyContain(k => values[k] == "false");
        EntitlementKeys.Limits.Should().OnlyContain(k => values[k] == "0");
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == "plan.created" && e.Actor == Operator);
    }

    [Fact]
    public async Task Create_CanStartFromAnotherPlansEntitlements()
    {
        var host = new SubscriptionTestHost();

        await Catalog(host).CreateAsync(new CreatePlanCommand { Code = "payroll-plus", Name = "Payroll Plus", CopyEntitlementsFromCode = "payroll" }, Operator);
        await host.SaveAsync();

        ValuesOf(host, "payroll-plus").Should().BeEquivalentTo(ValuesOf(host, "payroll"));
    }

    [Fact]
    public async Task Create_PutsTheNewPlanAfterTheExistingOnes()
    {
        var host = new SubscriptionTestHost();
        var highest = host.Uow.Context.Plans.Max(p => p.SortOrder);

        var plan = await Catalog(host).CreateAsync(new CreatePlanCommand { Code = "enterprise", Name = "Enterprise" }, Operator);

        plan.SortOrder.Should().Be(highest + 10);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("1abc")]
    [InlineData("has space")]
    [InlineData("-lead")]
    [InlineData("a-way-too-long-code-that-goes-past-the-forty-character-limit")]
    public async Task Create_RefusesABadCode(string code)
    {
        var host = new SubscriptionTestHost();

        await Assert.ThrowsAsync<SubscriptionRuleException>(() => Catalog(host).CreateAsync(new CreatePlanCommand { Code = code, Name = "X" }, Operator));
    }

    [Fact]
    public async Task Create_RefusesADuplicateCode_ABlankName_NegativePrices_AndAnUnknownSource()
    {
        var host = new SubscriptionTestHost();
        var catalog = Catalog(host);

        await Assert.ThrowsAsync<SubscriptionRuleException>(() => catalog.CreateAsync(new CreatePlanCommand { Code = "PAYROLL", Name = "Dup" }, Operator));
        await Assert.ThrowsAsync<SubscriptionRuleException>(() => catalog.CreateAsync(new CreatePlanCommand { Code = "ok-code", Name = "  " }, Operator));
        await Assert.ThrowsAsync<SubscriptionRuleException>(() => catalog.CreateAsync(new CreatePlanCommand { Code = "ok-code", Name = "X", MonthlyPrice = -1 }, Operator));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => catalog.CreateAsync(new CreatePlanCommand { Code = "ok-code", Name = "X", CopyEntitlementsFromCode = "nope" }, Operator));
        host.Uow.Context.Plans.Local.Should().NotContain(p => p.Code == "ok-code");
    }

    // ── Price edits ──────────────────────────────────────────────────────────

    private static async Task<(Guid Owner, SubscriptionTestHost Host)> SubscriberOnPayrollAsync()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        return (owner, host);
    }

    [Fact]
    public async Task PriceEdit_ByDefault_ReachesNewSubscribersOnly()
    {
        var (owner, host) = await SubscriberOnPayrollAsync();
        var lockedBefore = host.SubscriptionOf(owner).LockedMonthlyPrice;

        await Catalog(host).UpdateAsync("payroll", new UpdatePlanCommand { MonthlyPrice = 6000m }, Operator);
        await host.SaveAsync();

        host.Uow.Context.Plans.Single(p => p.Code == "payroll").MonthlyPrice.Should().Be(6000m);
        host.SubscriptionOf(owner).LockedMonthlyPrice.Should().Be(lockedBefore);
    }

    [Fact]
    public async Task PriceEdit_ApplyToExisting_RewritesTheLockOfTheChangedPriceOnly()
    {
        var (owner, host) = await SubscriberOnPayrollAsync();
        var annualBefore = host.SubscriptionOf(owner).LockedAnnualPrice;

        await Catalog(host).UpdateAsync("payroll", new UpdatePlanCommand { MonthlyPrice = 6000m, ApplyToExisting = true }, Operator);
        await host.SaveAsync();

        var subscription = host.SubscriptionOf(owner);
        subscription.LockedMonthlyPrice.Should().Be(6000m);
        subscription.LockedAnnualPrice.Should().Be(annualBefore, "the annual price wasn't edited");
        var price = await host.Subscriptions.PriceForAsync(subscription, subscription.Plan, BillingCycle.Monthly);
        price.Should().Be(6000m);
        host.Uow.Context.BillingEvents.Single(e => e.Type == "plan.updated").PayloadJson.Should().Contain("\"repricedAccounts\":1");
    }

    [Fact]
    public async Task PriceEdit_ApplyToExisting_LeavesPaidPeriodsAndOtherPlansAlone()
    {
        var (owner, host) = await SubscriberOnPayrollAsync();
        var paidBefore = host.Uow.Context.SubscriptionPayments.Select(p => p.Amount).ToList();
        var otherPlanLock = host.Uow.Context.AccountSubscriptions.Where(s => s.OwnerUserId != owner).Select(s => s.LockedMonthlyPrice).ToList();

        await Catalog(host).UpdateAsync("payroll", new UpdatePlanCommand { MonthlyPrice = 6000m, ApplyToExisting = true }, Operator);
        await host.SaveAsync();

        host.Uow.Context.SubscriptionPayments.Select(p => p.Amount).Should().BeEquivalentTo(paidBefore);
        host.Uow.Context.AccountSubscriptions.Where(s => s.OwnerUserId != owner).Select(s => s.LockedMonthlyPrice)
            .Should().BeEquivalentTo(otherPlanLock);
    }

    [Fact]
    public async Task PriceEdit_ApplyToOwners_ReachesOnlyTheChosenAccounts()
    {
        var (owner, host) = await SubscriberOnPayrollAsync();
        var lockedBefore = host.SubscriptionOf(owner).LockedMonthlyPrice;

        await Catalog(host).UpdateAsync("payroll", new UpdatePlanCommand { MonthlyPrice = 6000m, ApplyToOwners = [Guid.NewGuid()] }, Operator);
        await host.SaveAsync();
        host.SubscriptionOf(owner).LockedMonthlyPrice.Should().Be(lockedBefore, "this owner wasn't chosen");

        await Catalog(host).UpdateAsync("payroll", new UpdatePlanCommand { MonthlyPrice = 7000m, ApplyToOwners = [owner] }, Operator);
        await host.SaveAsync();
        host.SubscriptionOf(owner).LockedMonthlyPrice.Should().Be(7000m);
    }

    [Fact]
    public async Task PriceEdit_ApplyToExisting_DoesNothingWhenThePriceIsUnchanged()
    {
        var (owner, host) = await SubscriberOnPayrollAsync();
        var plan = host.Uow.Context.Plans.Single(p => p.Code == "payroll");
        var lockedBefore = host.SubscriptionOf(owner).LockedMonthlyPrice;

        await Catalog(host).UpdateAsync("payroll", new UpdatePlanCommand { MonthlyPrice = plan.MonthlyPrice, ApplyToExisting = true }, Operator);
        await host.SaveAsync();

        host.SubscriptionOf(owner).LockedMonthlyPrice.Should().Be(lockedBefore);
        host.Uow.Context.BillingEvents.Single(e => e.Type == "plan.updated").PayloadJson.Should().Contain("\"repricedAccounts\":0");
    }

    // ── Bulk entitlements ────────────────────────────────────────────────────

    [Fact]
    public async Task SetEntitlements_ChangesSeveralAtOnce_ReportsOnlyWhatChanged_AndRecordsIt()
    {
        var host = new SubscriptionTestHost();
        var before = ValuesOf(host, "payroll");
        var flip = before[EntitlementKeys.Analytics] == "true" ? "false" : "true";

        var changed = await Catalog(host).SetEntitlementsAsync("payroll", new Dictionary<string, string>
        {
            [EntitlementKeys.Analytics] = flip,
            [EntitlementKeys.EmployeesMax] = "777",
            [EntitlementKeys.PayrollCore] = before[EntitlementKeys.PayrollCore], // unchanged
        }, Operator);
        await host.SaveAsync();

        changed.Should().BeEquivalentTo([EntitlementKeys.Analytics, EntitlementKeys.EmployeesMax]);
        var after = ValuesOf(host, "payroll");
        after[EntitlementKeys.Analytics].Should().Be(flip);
        after[EntitlementKeys.EmployeesMax].Should().Be("777");
        var recorded = host.Uow.Context.BillingEvents.Single(e => e.Type == "plan.entitlements-changed");
        recorded.Actor.Should().Be(Operator);
        recorded.PayloadJson.Should().Contain("777");
    }

    [Fact]
    public async Task SetEntitlements_IsAllOrNothing_OneBadValueChangesNothing()
    {
        var host = new SubscriptionTestHost();
        var before = ValuesOf(host, "payroll");

        var act = () => Catalog(host).SetEntitlementsAsync("payroll", new Dictionary<string, string>
        {
            [EntitlementKeys.EmployeesMax] = "999",
            [EntitlementKeys.Analytics] = "maybe",
        }, Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
        await host.SaveAsync();
        ValuesOf(host, "payroll").Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task SetEntitlements_RefusesAnUnknownKey_AnUnknownPlan_AndAnEmptyRequest()
    {
        var host = new SubscriptionTestHost();
        var catalog = Catalog(host);

        await Assert.ThrowsAsync<SubscriptionRuleException>(() => catalog.SetEntitlementsAsync("payroll", new Dictionary<string, string> { ["made.up"] = "true" }, Operator));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => catalog.SetEntitlementsAsync("nope", new Dictionary<string, string> { [EntitlementKeys.Analytics] = "true" }, Operator));
        await Assert.ThrowsAsync<SubscriptionRuleException>(() => catalog.SetEntitlementsAsync("payroll", new Dictionary<string, string>(), Operator));
    }

    [Fact]
    public async Task SetEntitlements_TellsEveryAccountOnThePlan_OncePerAccount_AndNobodyWhenNothingChanged()
    {
        var host = new SubscriptionTestHost();
        var (a, _) = await host.TrialAccountAsync("a@acme.ph");
        var (b, _) = await host.TrialAccountAsync("b@acme.ph");
        await host.Subscriptions.SetPlanAsync(a, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.Subscriptions.SetPlanAsync(b, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        host.Publisher.Invocations.Clear();
        var current = ValuesOf(host, "payroll")[EntitlementKeys.EmployeesMax];

        await Catalog(host).SetEntitlementsAsync("payroll", new Dictionary<string, string> { [EntitlementKeys.EmployeesMax] = current }, Operator);
        host.Publisher.Verify(p => p.Publish(It.IsAny<SubscriptionChanged>(), It.IsAny<CancellationToken>()), Times.Never);

        await Catalog(host).SetEntitlementsAsync("payroll", new Dictionary<string, string> { [EntitlementKeys.EmployeesMax] = "123", [EntitlementKeys.AdminsMax] = "9" }, Operator);
        host.Publisher.Verify(p => p.Publish(It.Is<SubscriptionChanged>(m => m.OwnerUserId == a), It.IsAny<CancellationToken>()), Times.Once);
        host.Publisher.Verify(p => p.Publish(It.Is<SubscriptionChanged>(m => m.OwnerUserId == b), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Impact & subscribers ─────────────────────────────────────────────────

    [Fact]
    public async Task Impact_CountsAccountsOnThePlan_ByStatus()
    {
        var host = new SubscriptionTestHost();
        var (active, _) = await host.TrialAccountAsync("a@acme.ph");
        var (lapsed, _) = await host.TrialAccountAsync("b@acme.ph");
        await host.TrialAccountAsync("c@acme.ph"); // stays on the trial plan (premium)
        await host.Subscriptions.SetPlanAsync(active, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.Subscriptions.SetPlanAsync(lapsed, new SetPlanCommand { PlanCode = "payroll" }, "platform");
        host.SubscriptionOf(lapsed).SubStatus = AccountSubscriptionStatus.Expired;
        await host.SaveAsync();

        var impact = await Catalog(host).GetImpactAsync("payroll");

        impact.Accounts.Should().Be(2);
        impact.ByStatus.Should().Contain(new KeyValuePair<AccountSubscriptionStatus, int>(AccountSubscriptionStatus.Active, 1))
            .And.Contain(new KeyValuePair<AccountSubscriptionStatus, int>(AccountSubscriptionStatus.Expired, 1));
        (await Catalog(host).GetImpactAsync("timekeeping")).Accounts.Should().Be(0);
    }

    [Fact]
    public async Task Impact_OfAnUnknownPlan_IsNotFound()
    {
        var act = () => Catalog(new SubscriptionTestHost()).GetImpactAsync("nope");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task SubscriberCounts_AreKeyedByPlanId()
    {
        var host = new SubscriptionTestHost();
        await host.TrialAccountAsync("a@acme.ph");
        await host.TrialAccountAsync("b@acme.ph");

        var counts = await Catalog(host).GetSubscriberCountsAsync();

        counts[host.Uow.Context.Plans.Single(p => p.Code == "premium").Id].Should().Be(2);
    }
}
