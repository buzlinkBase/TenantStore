using FluentAssertions;
using Moq;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure.EntityConfigs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>
/// Storage blocks: who may buy which offering, what the blocks add to the account's one storage pool,
/// what they cost monthly and annually, and how they renew and shrink -- on storage's own billing
/// period, independent of the plan's.
/// </summary>
public class StorageAddOnTests
{
    private const string Block = "storage-50gb";
    private const int PayrollBaseMb = 20480;
    private const int BlockMb = 51200;

    /// <summary>A Payroll account (base 20 GB for the whole account) paid up for a month from the host's start date.</summary>
    private static async Task<(SubscriptionTestHost Host, Guid Owner, Guid TenantId)> PaidAccountAsync()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");
        await host.SaveAsync();
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999m, Method = PaymentMethod.BankTransfer }, "platform");
        await host.SaveAsync();
        return (host, owner, tenantId);
    }

    private static async Task<int?> LimitAsync(SubscriptionTestHost host, Guid tenantId) =>
        (await host.Resolver.ResolveActualForTenantAsync(tenantId)).LimitOf(EntitlementKeys.StorageMaxMb);

    private static async Task BuyAsync(SubscriptionTestHost host, Guid owner, int blocks, BillingCycle? cycle = null)
    {
        var quote = await host.Storage.QuoteAsync(owner, Block, blocks, cycle);
        await host.Storage.ActivateAsync(owner, Block, blocks, quote.AmountDue, PaymentMethod.GCash, "pay_1", "owner", cycle);
        await host.SaveAsync();
    }

    private static StorageSubscription StorageOf(SubscriptionTestHost host, Guid owner)
    {
        var subscriptionId = host.SubscriptionOf(owner).Id;
        return host.Uow.Context.StorageSubscriptions.Single(s => s.SubscriptionId == subscriptionId);
    }

    private static async Task<StorageRenewalDue?> StorageDueAsync(SubscriptionTestHost host, Guid owner) =>
        await host.StoragePeriod.GetRenewalDueAsync(host.SubscriptionOf(owner));

    private static async Task RenewStorageAsync(SubscriptionTestHost host, Guid owner)
    {
        var due = await StorageDueAsync(host, owner);
        await host.StoragePeriod.RecordRenewalAsync(owner, due!.Amount, PaymentMethod.GCash, "pay_renew", "owner");
        await host.SaveAsync();
    }

    private static async Task AdvanceAsync(SubscriptionTestHost host, Guid owner, DateTime to)
    {
        host.Clock.UtcNow = to;
        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);
        // The host's unit of work commits only once; later runs need saving by hand.
        await host.SaveAsync();
    }

    // ── Pricing rules ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Prorate_ChargesOnlyTheRestOfThePeriod()
    {
        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        StorageAddOnPricing.Prorate(400m, 1, start, end, start).Should().Be(400m);
        StorageAddOnPricing.Prorate(400m, 2, start, end, start.AddDays(15.5)).Should().Be(400m);
        StorageAddOnPricing.Prorate(400m, 1, start, end, end).Should().Be(0m);
        StorageAddOnPricing.Prorate(400m, 1, start, end, end.AddDays(3)).Should().Be(0m, "never negative");
    }

    [Fact]
    public void Prorate_OnAnAnnualPeriod_UsesTheAnnualPrice()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        StorageAddOnPricing.Prorate(4000m, 1, start, start.AddYears(1), start).Should().Be(4000m);
        StorageAddOnPricing.Prorate(4000m, 1, start, start.AddYears(1), start.AddDays(365.0 / 2)).Should().BeInRange(1990m, 2010m);
    }

    // ── Who may buy what ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublicOfferingIsVisibleToEveryAccount()
    {
        var host = new SubscriptionTestHost();

        (await host.StorageCatalog.GetVisibleAsync(Guid.NewGuid())).Select(o => o.Code).Should().Equal(Block);
        (await host.StorageCatalog.GetVisibleAsync(null)).Select(o => o.Code).Should().Equal(Block);
    }

    [Fact]
    public async Task NewOffering_StartsHidden_UntilOfferedOrMadePublic()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        var (other, _) = await host.TrialAccountAsync("other@acme.ph");

        await host.StorageCatalog.CreateAsync(new CreateStorageOfferingCommand
        {
            Code = "storage-200gb", Name = "200 GB block", BlockMb = 200 * 1024, MonthlyPrice = 1400m, AnnualPrice = 14000m,
        }, "platform");
        await host.SaveAsync();
        (await host.StorageCatalog.GetVisibleAsync(owner)).Select(o => o.Code).Should().Equal(Block);

        await host.StorageCatalog.OfferAsync("storage-200gb", owner, null, "platform");
        await host.SaveAsync();
        (await host.StorageCatalog.GetVisibleAsync(owner)).Select(o => o.Code).Should().Equal(Block, "storage-200gb");
        (await host.StorageCatalog.GetVisibleAsync(other)).Select(o => o.Code).Should().Equal([Block], "an offer is for one account only");

        await host.StorageCatalog.UpdateAsync("storage-200gb", new UpdateStorageOfferingCommand { IsPublic = true }, "platform");
        await host.SaveAsync();
        (await host.StorageCatalog.GetVisibleAsync(other)).Select(o => o.Code).Should().Contain("storage-200gb");
    }

    [Fact]
    public async Task Offer_Expires_AndCanBeRevoked()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.StorageCatalog.UpdateAsync(Block, new UpdateStorageOfferingCommand { IsPublic = false }, "platform");
        await host.StorageCatalog.OfferAsync(Block, owner, SubscriptionTestHost.Start.AddDays(10), "platform");
        await host.SaveAsync();

        (await host.StorageCatalog.GetVisibleAsync(owner)).Should().ContainSingle();

        host.Clock.Advance(TimeSpan.FromDays(11));
        (await host.StorageCatalog.GetVisibleAsync(owner)).Should().BeEmpty("the offer expired");

        await host.StorageCatalog.OfferAsync(Block, owner, null, "platform");
        await host.SaveAsync();
        (await host.StorageCatalog.GetVisibleAsync(owner)).Should().ContainSingle();

        await host.StorageCatalog.RevokeOfferAsync(Block, owner, "platform");
        await host.SaveAsync();
        (await host.StorageCatalog.GetVisibleAsync(owner)).Should().BeEmpty();
    }

    [Fact]
    public async Task InactiveOffering_IsNotForSale_EvenWhenPublic()
    {
        var host = new SubscriptionTestHost();
        await host.StorageCatalog.UpdateAsync(Block, new UpdateStorageOfferingCommand { IsActive = false }, "platform");
        await host.SaveAsync();

        (await host.StorageCatalog.GetVisibleAsync(Guid.NewGuid())).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_RejectsABadCodeADuplicateAndANonsensePrice()
    {
        var host = new SubscriptionTestHost();

        var badCode = () => host.StorageCatalog.CreateAsync(new CreateStorageOfferingCommand { Code = "Bad Code", Name = "x", BlockMb = 1, MonthlyPrice = 1 }, "p");
        var duplicate = () => host.StorageCatalog.CreateAsync(new CreateStorageOfferingCommand { Code = Block, Name = "x", BlockMb = 1, MonthlyPrice = 1 }, "p");
        var negative = () => host.StorageCatalog.CreateAsync(new CreateStorageOfferingCommand { Code = "storage-x", Name = "x", BlockMb = 1, MonthlyPrice = -1 }, "p");
        var negativeAnnual = () => host.StorageCatalog.CreateAsync(new CreateStorageOfferingCommand { Code = "storage-z", Name = "x", BlockMb = 1, MonthlyPrice = 1, AnnualPrice = -5 }, "p");
        var empty = () => host.StorageCatalog.CreateAsync(new CreateStorageOfferingCommand { Code = "storage-y", Name = "x", BlockMb = 0, MonthlyPrice = 1 }, "p");

        await badCode.Should().ThrowAsync<SubscriptionRuleException>();
        await duplicate.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*already exists*");
        await negative.Should().ThrowAsync<SubscriptionRuleException>();
        await negativeAnnual.Should().ThrowAsync<SubscriptionRuleException>();
        await empty.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task AnOffering_WithNoAnnualPrice_DefaultsToTwelveMonths_AndItCanBeDiscountedLater()
    {
        var host = new SubscriptionTestHost();

        var offering = await host.StorageCatalog.CreateAsync(new CreateStorageOfferingCommand
        {
            Code = "storage-100gb", Name = "100 GB block", BlockMb = 100 * 1024, MonthlyPrice = 700m,
        }, "platform");
        offering.AnnualPrice.Should().Be(8400m);
        await host.SaveAsync();

        await host.StorageCatalog.UpdateAsync("storage-100gb", new UpdateStorageOfferingCommand { AnnualPrice = 7000m }, "platform");
        offering.AnnualPrice.Should().Be(7000m);
        offering.MonthlyPrice.Should().Be(700m);
    }

    // ── Quote ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Quote_FirstPurchase_StartsAStoragePeriod_AtTheFullPrice()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(15.5));

        var quote = await host.Storage.QuoteAsync(owner, Block, 2);

        quote.StartsNewPeriod.Should().BeTrue();
        quote.AmountDue.Should().Be(800m, "storage gets its own period from today, not the rest of the plan's");
        quote.BillingCycle.Should().Be(BillingCycle.Monthly, "the plan's cycle is the default");
        quote.PeriodStartUtc.Should().Be(host.Clock.UtcNow);
        quote.PeriodEndUtc.Should().Be(host.Clock.UtcNow.AddMonths(1));
        quote.NewAddOnsTotal.Should().Be(800m);
        quote.NewLimitMb.Should().Be(PayrollBaseMb + 2 * BlockMb);
    }

    [Fact]
    public async Task Quote_ProratesToTheStorageDueDate_NotThePlans()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(10));
        await BuyAsync(host, owner, 1);
        var storageEnd = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        host.Clock.UtcNow = storageEnd.AddDays(-15.5);

        var quote = await host.Storage.QuoteAsync(owner, Block, 2);

        quote.StartsNewPeriod.Should().BeFalse();
        quote.PeriodEndUtc.Should().Be(storageEnd);
        quote.PeriodEndUtc.Should().NotBe(host.SubscriptionOf(owner).CurrentPeriodEndUtc!.Value);
        quote.AmountDue.Should().BeInRange(395m, 405m, "two blocks at 400, about half of the storage month left");
        quote.NewAddOnsTotal.Should().Be(1200m);
    }

    [Fact]
    public async Task Quote_StorageCanBeAnnual_OnAMonthlyPlan()
    {
        var (host, owner, _) = await PaidAccountAsync();

        var quote = await host.Storage.QuoteAsync(owner, Block, 1, BillingCycle.Annual);

        quote.BillingCycle.Should().Be(BillingCycle.Annual);
        quote.UnitPrice.Should().Be(4000m, "the annual price, not 12 x 400");
        quote.AmountDue.Should().Be(4000m);
        quote.PeriodEndUtc.Should().Be(host.Clock.UtcNow.AddYears(1));
    }

    [Fact]
    public async Task Quote_StorageCanBeMonthly_OnAnAnnualPlan()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Annual, "owner");
        await host.SaveAsync();
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 49990m, Method = PaymentMethod.BankTransfer }, "platform");
        await host.SaveAsync();

        (await host.Storage.QuoteAsync(owner, Block, 1)).BillingCycle.Should().Be(BillingCycle.Annual, "the plan's cycle is only the default");
        var monthly = await host.Storage.QuoteAsync(owner, Block, 1, BillingCycle.Monthly);

        monthly.UnitPrice.Should().Be(400m);
        monthly.AmountDue.Should().Be(400m);
        monthly.PeriodEndUtc.Should().Be(host.Clock.UtcNow.AddMonths(1));
    }

    [Fact]
    public async Task Quote_WhileAStoragePeriodRuns_IgnoresTheRequestedCycle()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1, BillingCycle.Monthly);

        var quote = await host.Storage.QuoteAsync(owner, Block, 1, BillingCycle.Annual);

        quote.BillingCycle.Should().Be(BillingCycle.Monthly, "every block shares one storage due date");
    }

    [Fact]
    public async Task Quote_DuringATrial_IsAllowed_StorageDoesNotNeedAPaidPlan()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        var quote = await host.Storage.QuoteAsync(owner, Block, 1);

        quote.AmountDue.Should().Be(400m);
        quote.StartsNewPeriod.Should().BeTrue();
    }

    [Fact]
    public async Task Quote_OnAnAccountSuspendedByAnOperator_IsRefused()
    {
        var (host, owner, _) = await PaidAccountAsync();
        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus = AccountSubscriptionStatus.Suspended;
        subscription.SuspensionReason = "fraud review";
        await host.SaveAsync();

        var act = () => host.Storage.QuoteAsync(owner, Block, 1);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*suspended*");
    }

    [Fact]
    public async Task Quote_WhileTheStorageRenewalIsUnpaid_IsRefused()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        await AdvanceAsync(host, owner, StorageOf(host, owner).CurrentPeriodEndUtc!.Value.AddDays(1));

        var act = () => host.Storage.QuoteAsync(owner, Block, 1);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*renewal is unpaid*");
        (await host.Storage.GetSummaryAsync(owner)).CanPurchase.Should().BeFalse();
    }

    [Fact]
    public async Task Quote_ForAnOfferingTheAccountCannotSee_IsRefused()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await host.StorageCatalog.UpdateAsync(Block, new UpdateStorageOfferingCommand { IsPublic = false }, "platform");
        await host.SaveAsync();

        var act = () => host.Storage.QuoteAsync(owner, Block, 1);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*isn't available*");
    }

    [Fact]
    public async Task Quote_NeedsAtLeastOneBlock()
    {
        var (host, owner, _) = await PaidAccountAsync();

        var act = () => host.Storage.QuoteAsync(owner, Block, 0);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    // ── Buying ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Activate_RaisesTheLimitAtOnce_AndTellsTheOtherServices()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb);
        host.Publisher.Invocations.Clear();

        await BuyAsync(host, owner, 2);

        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + 2 * BlockMb);
        host.Publisher.Verify(p => p.Publish(It.Is<SubscriptionChanged>(c => c.OwnerUserId == owner), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        host.Uow.Context.StorageAddOnPayments.Should().ContainSingle(p => p.Blocks == 2 && p.Kind == StorageAddOnPaymentKind.Purchase);
        host.Uow.Context.BillingEvents.Should().Contain(e => e.Type == BillingEventTypes.StorageAddOnPurchased && e.OwnerUserId == owner);
    }

    [Fact]
    public async Task Activate_StartsTheStoragePeriod_OnTheChosenCycle()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(5));

        await BuyAsync(host, owner, 1, BillingCycle.Annual);

        var storage = StorageOf(host, owner);
        storage.Status.Should().Be(StorageSubscriptionStatus.Active);
        storage.BillingCycle.Should().Be(BillingCycle.Annual);
        storage.CurrentPeriodStartUtc.Should().Be(host.Clock.UtcNow);
        storage.CurrentPeriodEndUtc.Should().Be(host.Clock.UtcNow.AddYears(1));
        host.SubscriptionOf(owner).BillingCycle.Should().Be(BillingCycle.Monthly, "the plan is untouched");
        host.Uow.Context.StorageAddOnPayments.Single().PeriodEndUtc.Should().Be(storage.CurrentPeriodEndUtc!.Value);
    }

    [Fact]
    public async Task Activate_Twice_AddsToTheSameHolding()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();

        await BuyAsync(host, owner, 1);
        await BuyAsync(host, owner, 3);

        host.Uow.Context.StorageAddOns.Should().ContainSingle(a => a.Blocks == 4);
        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + 4 * BlockMb);
    }

    [Fact]
    public async Task Blocks_AreOnePool_ForEveryCompanyOfTheAccount()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        var second = host.AddCompany(owner, "Second");

        await BuyAsync(host, owner, 1);

        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + BlockMb);
        (await LimitAsync(host, second)).Should().Be(PayrollBaseMb + BlockMb, "storage belongs to the account, not to one company");
    }

    [Fact]
    public async Task TheSnapshot_NamesEveryCompanyThatSharesThePool()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        var second = host.AddCompany(owner, "Second");
        host.AddCompany(Guid.NewGuid(), "Someone else's");

        var snapshot = await host.Resolver.ResolveActualForTenantAsync(tenantId);

        snapshot.AccountTenantIds.Should().BeEquivalentTo([tenantId, second]);
    }

    [Fact]
    public async Task UnlimitedStorage_StaysUnlimited_WithBlocks()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        host.Uow.Context.PlanEntitlements
            .Single(e => e.PlanId == PlanSeed.PayrollId && e.Key == EntitlementKeys.StorageMaxMb).Value = "-1";
        await host.SaveAsync();

        await BuyAsync(host, owner, 1);

        (await LimitAsync(host, tenantId)).Should().BeNull();
    }

    [Fact]
    public async Task AnAccountOverride_StillReplacesTheTotal()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);

        await host.Subscriptions.AddOverrideAsync(owner, new AddOverrideCommand { Key = EntitlementKeys.StorageMaxMb, Value = "1000" }, "platform");
        await host.SaveAsync();

        (await LimitAsync(host, tenantId)).Should().Be(1000);
    }

    [Fact]
    public async Task StorageCannotBeOverriddenForOneCompany()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();

        var act = () => host.Subscriptions.AddOverrideAsync(owner,
            new AddOverrideCommand { Key = EntitlementKeys.StorageMaxMb, Value = "1000", TenantId = tenantId }, "platform");

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*whole account*");
    }

    // ── The plan no longer bills storage ─────────────────────────────────────────────────────

    [Fact]
    public async Task PlanRenewal_DoesNotIncludeStorage()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 2);
        await host.Storage.GrantAsync(owner, Block, 5, "goodwill", "platform");
        await host.SaveAsync();
        host.Clock.Advance(TimeSpan.FromDays(26));

        var due = await host.Subscriptions.GetPaymentDueAsync(host.SubscriptionOf(owner));

        due!.Reason.Should().Be(PaymentDueReason.Renewal);
        due.Amount.Should().Be(4999m, "storage is billed on its own");
        due.PlanAmount.Should().Be(4999m);
    }

    [Fact]
    public async Task PayingThePlan_LeavesTheStoragePeriodAlone()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(10));
        await BuyAsync(host, owner, 1);
        var storageEnd = StorageOf(host, owner).CurrentPeriodEndUtc;
        host.Clock.Advance(TimeSpan.FromDays(16));

        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999m, Method = PaymentMethod.BankTransfer }, "platform");
        await host.SaveAsync();

        StorageOf(host, owner).CurrentPeriodEndUtc.Should().Be(storageEnd);
        host.Uow.Context.SubscriptionPayments.OrderBy(p => p.PaidAtUtc).Last().LineItemsJson.Should().NotContain("Storage");
    }

    [Fact]
    public async Task ALapsedPlan_KeepsPaidStorage_UntilStoragesOwnDueDate()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1, BillingCycle.Annual);
        var planEnd = host.SubscriptionOf(owner).CurrentPeriodEndUtc!.Value;

        await AdvanceAsync(host, owner, planEnd.AddDays(host.Options.GraceDays + 1));

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Suspended);
        StorageOf(host, owner).Status.Should().Be(StorageSubscriptionStatus.Active);
        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + BlockMb, "the storage year is paid for");
    }

    // ── Storage renewal ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StorageRenewal_IsDue_InItsOwnWindow_NotThePlans()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 2, BillingCycle.Annual);
        await host.Storage.GrantAsync(owner, Block, 5, "goodwill", "platform");
        await host.SaveAsync();

        host.Clock.Advance(TimeSpan.FromDays(26));
        (await StorageDueAsync(host, owner)).Should().BeNull("the plan's window is open, storage's isn't");

        host.Clock.UtcNow = StorageOf(host, owner).CurrentPeriodEndUtc!.Value.AddDays(-5);
        var due = await StorageDueAsync(host, owner);

        due!.Reason.Should().Be(StorageDueReason.Renewal);
        due.BillingCycle.Should().Be(BillingCycle.Annual);
        due.Amount.Should().Be(8000m, "2 paid blocks at the 4,000 annual price; the free ones cost nothing");
        due.Blocks.Should().Be(2);
        due.PeriodStartUtc.Should().Be(StorageOf(host, owner).CurrentPeriodEndUtc!.Value);
    }

    [Fact]
    public async Task StorageRenewal_ExtendsFromTheDueDate()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        host.Clock.UtcNow = end.AddDays(-3);

        await RenewStorageAsync(host, owner);

        StorageOf(host, owner).CurrentPeriodStartUtc.Should().Be(end);
        StorageOf(host, owner).CurrentPeriodEndUtc.Should().Be(end.AddMonths(1));
        host.Uow.Context.StorageAddOnPayments.Should().Contain(p => p.Kind == StorageAddOnPaymentKind.Renewal && p.Amount == 400m && p.Blocks == 1);
        host.Uow.Context.BillingEvents.Should().Contain(e => e.Type == BillingEventTypes.StorageRenewed);
    }

    [Fact]
    public async Task StorageRenewal_OutsideItsWindow_IsRefused()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);

        var act = () => host.StoragePeriod.RecordRenewalAsync(owner, 400m, PaymentMethod.GCash, null, "owner");

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*no storage renewal due*");
    }

    [Fact]
    public async Task ABlockKeepsThePriceItWasBoughtAt_WhenTheCatalogChanges()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);

        await host.StorageCatalog.UpdateAsync(Block, new UpdateStorageOfferingCommand { MonthlyPrice = 999m, AnnualPrice = 9000m }, "platform");
        await host.SaveAsync();
        host.Clock.UtcNow = StorageOf(host, owner).CurrentPeriodEndUtc!.Value.AddDays(-2);

        (await StorageDueAsync(host, owner))!.Amount.Should().Be(400m);
    }

    [Fact]
    public async Task CycleSwitch_AppliesAtTheNextRenewal()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 2, BillingCycle.Monthly);

        await host.StoragePeriod.RequestCycleChangeAsync(owner, BillingCycle.Annual, "owner");
        await host.SaveAsync();
        StorageOf(host, owner).BillingCycle.Should().Be(BillingCycle.Monthly, "the paid month runs out first");
        (await host.Storage.GetSummaryAsync(owner)).PendingBillingCycle.Should().Be(BillingCycle.Annual);

        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        host.Clock.UtcNow = end.AddDays(-2);
        (await StorageDueAsync(host, owner))!.Amount.Should().Be(8000m, "2 blocks at the annual price");
        await RenewStorageAsync(host, owner);

        var storage = StorageOf(host, owner);
        storage.BillingCycle.Should().Be(BillingCycle.Annual);
        storage.PendingBillingCycle.Should().BeNull();
        storage.CurrentPeriodEndUtc.Should().Be(end.AddYears(1));
    }

    [Fact]
    public async Task CycleSwitch_CanBeWithdrawn()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        await host.StoragePeriod.RequestCycleChangeAsync(owner, BillingCycle.Annual, "owner");
        await host.SaveAsync();

        await host.StoragePeriod.WithdrawCycleChangeAsync(owner, "owner");
        await host.SaveAsync();

        StorageOf(host, owner).PendingBillingCycle.Should().BeNull();
        var again = () => host.StoragePeriod.RequestCycleChangeAsync(owner, BillingCycle.Monthly, "owner");
        await again.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*already billed monthly*");
    }

    // ── Storage lapse ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnpaidStorage_GoesPastDue_KeepsItsCapacityThroughTheGrace_ThenLapses()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1, BillingCycle.Annual);
        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        // Keep the plan paid throughout, so only storage lapses.
        host.SubscriptionOf(owner).CurrentPeriodEndUtc = end.AddYears(1);
        await host.SaveAsync();

        await AdvanceAsync(host, owner, end.AddHours(1));
        StorageOf(host, owner).Status.Should().Be(StorageSubscriptionStatus.PastDue);
        StorageOf(host, owner).GraceEndsAtUtc.Should().Be(end.AddDays(host.Options.StorageGraceDays));
        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + BlockMb, "full capacity during the grace");
        (await StorageDueAsync(host, owner))!.Reason.Should().Be(StorageDueReason.Renewal);

        await AdvanceAsync(host, owner, end.AddDays(host.Options.StorageGraceDays).AddHours(1));
        StorageOf(host, owner).Status.Should().Be(StorageSubscriptionStatus.Lapsed);
        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb, "back to what the plan includes");
        host.Uow.Context.StorageAddOns.Should().ContainSingle("nothing is deleted -- paying brings it back");
        host.Uow.Context.BillingEvents.Should().Contain(e => e.Type == BillingEventTypes.StoragePastDue);
        host.Uow.Context.BillingEvents.Should().Contain(e => e.Type == BillingEventTypes.StorageLapsed);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active, "the plan is untouched");
    }

    [Fact]
    public async Task StorageEmails_GoOutOnStoragesOwnDates_OnceEach()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1, BillingCycle.Annual);
        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        host.SubscriptionOf(owner).CurrentPeriodEndUtc = end.AddYears(1);
        await host.SaveAsync();

        int Sent(string template) => host.Publisher.Invocations
            .SelectMany(i => i.Arguments.OfType<SubscriptionReminderEmail>())
            .Count(e => e.Template == template);

        await AdvanceAsync(host, owner, end.AddDays(-5));
        await AdvanceAsync(host, owner, end.AddDays(-4));
        Sent(SubscriptionEmailTemplates.StorageRenewalDue).Should().Be(1);
        host.Publisher.Invocations.SelectMany(i => i.Arguments.OfType<SubscriptionReminderEmail>())
            .Single(e => e.Template == SubscriptionEmailTemplates.StorageRenewalDue).PeriodEndUtc.Should().Be(end);

        await AdvanceAsync(host, owner, end.AddHours(1));
        Sent(SubscriptionEmailTemplates.StoragePastDue).Should().Be(1);

        await AdvanceAsync(host, owner, end.AddDays(host.Options.StorageGraceDays + 1));
        Sent(SubscriptionEmailTemplates.StorageLapsed).Should().Be(1);
        Sent(SubscriptionEmailTemplates.PaymentDue).Should().Be(0, "the plan isn't due -- only storage is");
    }

    [Fact]
    public async Task CapacityFollowsTheDates_EvenBeforeTheJobRuns()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        var subscription = host.SubscriptionOf(owner);
        var storage = StorageOf(host, owner);
        storage.Status = StorageSubscriptionStatus.PastDue;
        storage.GraceEndsAtUtc = storage.CurrentPeriodEndUtc;
        subscription.CurrentPeriodEndUtc = storage.CurrentPeriodEndUtc!.Value.AddYears(1);
        await host.SaveAsync();

        host.Clock.UtcNow = storage.CurrentPeriodEndUtc!.Value.AddMinutes(1);

        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb);
    }

    [Fact]
    public async Task PayingLapsedStorage_StartsANewPeriodFromToday()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 2);
        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        host.SubscriptionOf(owner).CurrentPeriodEndUtc = end.AddYears(1);
        await host.SaveAsync();
        await AdvanceAsync(host, owner, end.AddDays(host.Options.StorageGraceDays + 3));

        var due = await StorageDueAsync(host, owner);
        due!.Reason.Should().Be(StorageDueReason.Reactivation);
        due.Amount.Should().Be(800m);
        await RenewStorageAsync(host, owner);

        var storage = StorageOf(host, owner);
        storage.Status.Should().Be(StorageSubscriptionStatus.Active);
        storage.CurrentPeriodStartUtc.Should().Be(host.Clock.UtcNow);
        storage.CurrentPeriodEndUtc.Should().Be(host.Clock.UtcNow.AddMonths(1));
        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + 2 * BlockMb);
    }

    [Fact]
    public async Task FreeGrants_KeepCounting_WhenPaidStorageLapses()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        await host.Storage.GrantAsync(owner, Block, 1, "goodwill", "platform");
        await host.SaveAsync();
        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        host.SubscriptionOf(owner).CurrentPeriodEndUtc = end.AddYears(1);
        await host.SaveAsync();

        await AdvanceAsync(host, owner, end.AddDays(host.Options.StorageGraceDays + 1));

        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + BlockMb, "the grant has no due date");
    }

    [Fact]
    public async Task VoidingAStorageRenewal_HandsItsPeriodBack()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        host.Clock.UtcNow = end.AddDays(-2);
        await RenewStorageAsync(host, owner);
        var renewal = host.Uow.Context.StorageAddOnPayments.Single(p => p.Kind == StorageAddOnPaymentKind.Renewal);

        await host.StoragePeriod.VoidPaymentAsync(renewal, "duplicate", VoidKind.EntryError, "platform");
        await host.SaveAsync();

        StorageOf(host, owner).CurrentPeriodEndUtc.Should().Be(end);
        renewal.VoidedAtUtc.Should().NotBeNull();
        (await host.Storage.GetPaymentsAsync(host.SubscriptionOf(owner).Id)).Should().NotContain(p => p.Payment.Id == renewal.Id);
    }

    // ── Reducing ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reduction_TakesEffectAtTheStorageDueDate_NotBefore()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 3);
        var addOn = host.Uow.Context.StorageAddOns.Single();

        await host.Storage.RequestReductionAsync(owner, addOn.Id, 1, "owner");
        await host.SaveAsync();

        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + 3 * BlockMb, "the capacity is paid for until the storage period ends");
        var summary = await host.Storage.GetSummaryAsync(owner);
        summary.ProjectedLimitMb.Should().Be(PayrollBaseMb + BlockMb);

        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        host.Clock.UtcNow = end.AddDays(-3);
        (await StorageDueAsync(host, owner))!.Amount.Should().Be(400m, "the renewal is already for the reduced blocks");

        host.Clock.UtcNow = end.AddMinutes(1);
        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + BlockMb, "entitlements follow the date");
    }

    [Fact]
    public async Task Reduction_IsMadePermanent_ByTheLifecycleJob()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 3);
        await host.Storage.RequestReductionAsync(owner, host.Uow.Context.StorageAddOns.Single().Id, 1, "owner");
        await host.SaveAsync();

        await AdvanceAsync(host, owner, StorageOf(host, owner).CurrentPeriodEndUtc!.Value.AddMinutes(1));

        host.Uow.Context.StorageAddOns.Single().Blocks.Should().Be(1);
        host.Uow.Context.StorageAddOns.Single().PendingBlocks.Should().BeNull();
        host.Uow.Context.BillingEvents.Should().Contain(e => e.Type == BillingEventTypes.StorageReductionApplied);
    }

    [Fact]
    public async Task ReducingEveryPaidBlockToZero_EndsStorageAtTheDueDate_WithNothingToPay()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        await host.Storage.RequestReductionAsync(owner, host.Uow.Context.StorageAddOns.Single().Id, 0, "owner");
        await host.SaveAsync();
        StorageOf(host, owner).CancelAtPeriodEnd.Should().BeTrue();

        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        host.Clock.UtcNow = end.AddDays(-2);
        (await StorageDueAsync(host, owner)).Should().BeNull();

        await AdvanceAsync(host, owner, end.AddMinutes(1));

        host.Uow.Context.StorageAddOns.Should().BeEmpty();
        StorageOf(host, owner).Status.Should().Be(StorageSubscriptionStatus.Lapsed, "no grace for storage given up on purpose");
        (await LimitAsync(host, tenantId)).Should().BeLessOrEqualTo(PayrollBaseMb);
        host.Publisher.Invocations.SelectMany(i => i.Arguments.OfType<SubscriptionReminderEmail>())
            .Should().NotContain(e => e.Template == SubscriptionEmailTemplates.StorageLapsed);
    }

    [Fact]
    public async Task ReducingLapsedStorage_AppliesAtOnce()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 2);
        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        host.SubscriptionOf(owner).CurrentPeriodEndUtc = end.AddYears(1);
        await host.SaveAsync();
        await AdvanceAsync(host, owner, end.AddDays(host.Options.StorageGraceDays + 1));

        await host.Storage.RequestReductionAsync(owner, host.Uow.Context.StorageAddOns.Single().Id, 0, "owner");
        await host.SaveAsync();

        host.Uow.Context.StorageAddOns.Should().BeEmpty();
        (await host.Storage.QuoteAsync(owner, Block, 1)).StartsNewPeriod.Should().BeTrue("nothing is owed any more, so storage can start afresh");
    }

    [Fact]
    public async Task Reduction_ToTheSameOrMoreBlocks_IsRefused_AndCanBeWithdrawn()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 2);
        var id = host.Uow.Context.StorageAddOns.Single().Id;

        var same = () => host.Storage.RequestReductionAsync(owner, id, 2, "owner");
        await same.Should().ThrowAsync<SubscriptionRuleException>();

        await host.Storage.RequestReductionAsync(owner, id, 0, "owner");
        await host.SaveAsync();
        await host.Storage.WithdrawReductionAsync(owner, id, "owner");
        await host.SaveAsync();
        host.Uow.Context.StorageAddOns.Single().PendingBlocks.Should().BeNull();
        StorageOf(host, owner).CancelAtPeriodEnd.Should().BeFalse();
    }

    [Fact]
    public async Task Summary_WarnsWhenUsageExceedsTheReducedLimit()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 2);
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = PayrollBaseMb + 80_000;
        await host.Storage.RequestReductionAsync(owner, host.Uow.Context.StorageAddOns.Single().Id, 1, "owner");
        await host.SaveAsync();

        var summary = await host.Storage.GetSummaryAsync(owner);

        summary.OverProjectedLimit.Should().BeTrue();
        summary.UsedMb.Should().Be(PayrollBaseMb + 80_000);
    }

    // ── Grants ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Grant_AddsCapacityForFree_AndCanBeRemoved()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();

        var granted = await host.Storage.GrantAsync(owner, Block, 2, "contract term", "platform");
        await host.SaveAsync();

        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + 2 * BlockMb);
        (await host.Storage.GetSummaryAsync(owner)).AddOnsTotal.Should().Be(0m);

        await host.Storage.RemoveAsync(owner, granted.Id, "ended", "platform");
        await host.SaveAsync();
        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb);
    }

    [Fact]
    public async Task Grant_NeedsAtLeastOneBlock()
    {
        var (host, owner, _) = await PaidAccountAsync();

        var none = () => host.Storage.GrantAsync(owner, Block, 0, null, "platform");

        await none.Should().ThrowAsync<SubscriptionRuleException>();
    }

    // ── Summary ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_ReportsUsageAgainstTheEffectiveLimit()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        var limit = PayrollBaseMb + BlockMb;
        var used = limit * 96 / 100 + 1;
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = used;

        var summary = await host.Storage.GetSummaryAsync(owner);

        summary.BaseMb.Should().Be(PayrollBaseMb);
        summary.AddOnMb.Should().Be(BlockMb);
        summary.LimitMb.Should().Be(limit);
        summary.PercentUsed.Should().Be(96);
        summary.Level.Should().Be(StorageLevel.Critical);
        summary.AvailableMb.Should().Be(limit - used);
        summary.BillingCycle.Should().Be(BillingCycle.Monthly);
        summary.AddOnsTotal.Should().Be(400m);
        summary.Status.Should().Be(StorageSubscriptionStatus.Active);
        summary.PeriodEndUtc.Should().Be(StorageOf(host, owner).CurrentPeriodEndUtc);
        summary.AddOns.Single().RenewsAtUtc.Should().Be(summary.PeriodEndUtc);
        summary.RenewalDue.Should().BeNull();
        summary.CanPurchase.Should().BeTrue();
        summary.Offerings.Should().ContainSingle(o => o.Code == Block);
    }

    [Fact]
    public async Task Summary_BeforeAnyStorageIsBought_HasNoStoragePeriod()
    {
        var (host, owner, _) = await PaidAccountAsync();

        var summary = await host.Storage.GetSummaryAsync(owner);

        summary.Status.Should().BeNull();
        summary.PeriodEndUtc.Should().BeNull();
        summary.BillingCycle.Should().Be(BillingCycle.Monthly, "the plan's cycle, offered as the default");
        summary.CanPurchase.Should().BeTrue();
    }

    [Fact]
    public async Task Summary_ShowsTheStorageDue_InItsRenewalWindow()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        host.Clock.UtcNow = StorageOf(host, owner).CurrentPeriodEndUtc!.Value.AddDays(-2);

        var summary = await host.Storage.GetSummaryAsync(owner);

        summary.RenewalDue!.Amount.Should().Be(400m);
        summary.RenewalDue.LineItems.Should().ContainSingle();
    }

    [Fact]
    public async Task Summary_AddsUpEveryCompany_AgainstTheOnePool()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        var second = host.AddCompany(owner, "Second");
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = 6000;
        host.HrmsUsage.Usage[second][EntitlementKeys.StorageMaxMb] = 7000;

        var summary = await host.Storage.GetSummaryAsync(owner);

        summary.UsedMb.Should().Be(13000, "storage is one pool for the account");
        summary.Companies.Should().HaveCount(2);
        summary.Companies.Single(c => c.TenantId == second).UsedMb.Should().Be(7000);
        summary.PercentUsed.Should().Be(63);
    }

    [Fact]
    public async Task Summary_OnAnAnnualAccount_ShowsAnnualPrices()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Annual, "owner");
        await host.SaveAsync();
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 49990m, Method = PaymentMethod.BankTransfer }, "platform");
        await host.SaveAsync();
        await BuyAsync(host, owner, 1);

        var summary = await host.Storage.GetSummaryAsync(owner);

        summary.BillingCycle.Should().Be(BillingCycle.Annual);
        summary.AddOnsTotal.Should().Be(4000m);
        summary.AddOns.Single().UnitPrice.Should().Be(4000m);
    }

    [Fact]
    public async Task Summary_WhenAnyCompanysUsageIsUnknown_TheTotalIsUnknown_NotLow()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        var second = host.AddCompany(owner, "Second");
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = 100;
        host.HrmsUsage.Usage[second].Remove(EntitlementKeys.StorageMaxMb);

        var summary = await host.Storage.GetSummaryAsync(owner);

        summary.UsedMb.Should().BeNull();
        summary.Level.Should().Be(StorageLevel.Normal);
    }

    [Theory]
    [InlineData(0, 100, StorageLevel.Normal)]
    [InlineData(79, 100, StorageLevel.Normal)]
    [InlineData(80, 100, StorageLevel.Warning)]
    [InlineData(94, 100, StorageLevel.Warning)]
    [InlineData(95, 100, StorageLevel.Critical)]
    [InlineData(150, 100, StorageLevel.Critical)]
    public void Level_FollowsTheEightyAndNinetyFivePercentThresholds(int used, int limit, StorageLevel expected) =>
        StorageSizing.LevelOf(used, limit).Should().Be(expected);

    [Fact]
    public void Level_OfAnUnlimitedPlan_IsAlwaysNormal() =>
        StorageSizing.LevelOf(1_000_000, null).Should().Be(StorageLevel.Normal);

    // ── The 95% email ────────────────────────────────────────────────────────────────────────

    private static int EmailsAbout(SubscriptionTestHost host) =>
        host.Publisher.Invocations.Count(i =>
            i.Arguments.OfType<SubscriptionReminderEmail>().Any(e => e.Template == SubscriptionEmailTemplates.StorageAlmostFull));

    [Fact]
    public async Task AlmostFullStorage_EmailsTheOwner_OncePerBillingPeriod()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = PayrollBaseMb * 96 / 100;
        var id = host.SubscriptionOf(owner).Id;

        await host.Lifecycle.ProcessAsync(id, CancellationToken.None);
        await host.Lifecycle.ProcessAsync(id, CancellationToken.None);

        EmailsAbout(host).Should().Be(1, "the hourly run must not repeat it");
        host.Publisher.Verify(p => p.Publish(
            It.Is<SubscriptionReminderEmail>(e => e.Template == SubscriptionEmailTemplates.StorageAlmostFull
                && e.Email == "owner@acme.ph" && e.StoragePercent == 95 && e.StorageLimitMb == PayrollBaseMb),
            It.IsAny<CancellationToken>()), Times.Once);
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == BillingEventTypes.StorageThresholdCrossed);
    }

    [Fact]
    public async Task AlmostFullStorage_AcrossSeveralCompanies_IsOneEmail()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        var second = host.AddCompany(owner, "Second");
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = PayrollBaseMb / 2;
        host.HrmsUsage.Usage[second][EntitlementKeys.StorageMaxMb] = PayrollBaseMb * 46 / 100;

        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);

        EmailsAbout(host).Should().Be(1, "96% of the one pool, though neither company is near it alone");
    }

    [Fact]
    public async Task AlmostFullStorage_EmailsAgain_ForTheNextBillingPeriod()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = PayrollBaseMb * 96 / 100;
        var id = host.SubscriptionOf(owner).Id;
        await host.Lifecycle.ProcessAsync(id, CancellationToken.None);

        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999m, Method = PaymentMethod.BankTransfer }, "platform");
        await host.SaveAsync();
        await host.Lifecycle.ProcessAsync(id, CancellationToken.None);

        EmailsAbout(host).Should().Be(2);
    }

    [Theory]
    [InlineData(94)]
    [InlineData(0)]
    public async Task StorageBelowNinetyFivePercent_SendsNothing(int percent)
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = PayrollBaseMb * percent / 100;

        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);

        EmailsAbout(host).Should().Be(0);
    }

    [Fact]
    public async Task UnknownStorageUsage_SendsNothing()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        host.HrmsUsage.Usage[tenantId].Remove(EntitlementKeys.StorageMaxMb);

        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);

        EmailsAbout(host).Should().Be(0);
    }

    [Fact]
    public async Task BoughtBlocks_RaiseTheThreshold()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = PayrollBaseMb * 96 / 100;
        await BuyAsync(host, owner, 1);

        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);

        EmailsAbout(host).Should().Be(0, "96% of the base is only about 27% of base plus a block");
    }

    // ── Downgrades see the blocks ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Downgrade_IsRefused_WhenStorageUsedExceedsTheSmallerPlan()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = 10_000;

        var act = () => host.Subscriptions.RequestPlanChangeAsync(owner, "timekeeping", BillingCycle.Monthly, "owner");

        await act.Should().ThrowAsync<PlanLimitExceededException>("Timekeeping has 5 GB and 10,000 MB are in use");
    }

    [Fact]
    public async Task Downgrade_CountsEveryCompanyTogether()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        var second = host.AddCompany(owner, "Second");
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = 3000;
        host.HrmsUsage.Usage[second][EntitlementKeys.StorageMaxMb] = 3000;

        var act = () => host.Subscriptions.RequestPlanChangeAsync(owner, "timekeeping", BillingCycle.Monthly, "owner");

        await act.Should().ThrowAsync<PlanLimitExceededException>("neither company exceeds 5 GB alone, but together they use 6,000 MB");
    }

    [Fact]
    public async Task Downgrade_IsAllowed_WhenBlocksCoverTheUsage()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        host.HrmsUsage.Usage[tenantId][EntitlementKeys.StorageMaxMb] = 10_000;

        var quote = await host.Subscriptions.RequestPlanChangeAsync(owner, "timekeeping", BillingCycle.Monthly, "owner");

        quote.Timing.Should().Be(PlanChangeTiming.AtPeriodEnd);
    }

    [Fact]
    public async Task Downgrade_IsRefused_WhenAnyCompanysStorageCouldNotBeMeasured()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        host.HrmsUsage.Usage[tenantId].Remove(EntitlementKeys.StorageMaxMb);

        var act = () => host.Subscriptions.RequestPlanChangeAsync(owner, "timekeeping", BillingCycle.Monthly, "owner");

        await act.Should().ThrowAsync<PlanLimitExceededException>("unknown fails closed");
    }
}
