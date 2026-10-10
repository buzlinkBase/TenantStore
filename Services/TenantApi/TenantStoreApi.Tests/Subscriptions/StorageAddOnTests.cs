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
/// what they cost monthly and annually, and how they renew and shrink.
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

    private static async Task BuyAsync(SubscriptionTestHost host, Guid owner, int blocks)
    {
        var quote = await host.Storage.QuoteAsync(owner, Block, blocks);
        await host.Storage.ActivateAsync(owner, Block, blocks, quote.AmountDue, PaymentMethod.GCash, "pay_1", "owner");
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
    public async Task Quote_ProratesToThePeriodEnd_AndShowsTheNewCeiling()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(15.5));

        var quote = await host.Storage.QuoteAsync(owner, Block, 2);

        quote.AmountDue.Should().Be(400m, "two blocks at 400, half the month left");
        quote.BillingCycle.Should().Be(BillingCycle.Monthly);
        quote.UnitPrice.Should().Be(400m);
        quote.NewAddOnsTotal.Should().Be(800m);
        quote.NewLimitMb.Should().Be(PayrollBaseMb + 2 * BlockMb);
    }

    [Fact]
    public async Task Quote_OnAnAnnualAccount_UsesTheAnnualPrice()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Annual, "owner");
        await host.SaveAsync();
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 49990m, Method = PaymentMethod.BankTransfer }, "platform");
        await host.SaveAsync();

        var quote = await host.Storage.QuoteAsync(owner, Block, 1);

        quote.BillingCycle.Should().Be(BillingCycle.Annual);
        quote.UnitPrice.Should().Be(4000m, "the annual price, not 12 x 400");
        quote.AmountDue.Should().Be(4000m, "bought on the first day of the year");
        quote.NewAddOnsTotal.Should().Be(4000m);
    }

    [Fact]
    public async Task Quote_DuringATrial_IsRefused()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        var act = () => host.Storage.QuoteAsync(owner, Block, 1);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*paid and running*");
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
        host.Uow.Context.StorageAddOnPayments.Should().ContainSingle(p => p.Blocks == 2);
        host.Uow.Context.BillingEvents.Should().Contain(e => e.Type == BillingEventTypes.StorageAddOnPurchased && e.OwnerUserId == owner);
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

    // ── Renewal ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Renewal_ChargesThePlan_PlusPaidBlocks_ButNotFreeOnes()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 2);
        await host.Storage.GrantAsync(owner, Block, 5, "goodwill", "platform");
        await host.SaveAsync();
        host.Clock.Advance(TimeSpan.FromDays(26));

        var due = await host.Subscriptions.GetPaymentDueAsync(host.SubscriptionOf(owner));

        due!.Reason.Should().Be(PaymentDueReason.Renewal);
        due.AddOnsAmount.Should().Be(800m);
        due.Amount.Should().Be(4999m + 800m);
    }

    [Fact]
    public async Task Renewal_OnAnAnnualCycle_ChargesTheAnnualBlockPrice()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Annual, "owner");
        await host.SaveAsync();
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 49990m, Method = PaymentMethod.BankTransfer }, "platform");
        await host.SaveAsync();
        await BuyAsync(host, owner, 2);
        host.Clock.Advance(TimeSpan.FromDays(360));

        var due = await host.Subscriptions.GetPaymentDueAsync(host.SubscriptionOf(owner));

        due!.AddOnsAmount.Should().Be(8000m, "2 blocks at the 4,000 annual price -- a discount on 2 x 12 x 400 = 9,600");
        due.Amount.Should().Be(49990m + 8000m);
    }

    [Fact]
    public async Task APlanWithNoBlocks_IsDueExactlyItsPrice()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(26));

        var due = await host.Subscriptions.GetPaymentDueAsync(host.SubscriptionOf(owner));

        due!.Amount.Should().Be(4999m);
        due.AddOnsAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ABlockKeepsThePriceItWasBoughtAt_WhenTheCatalogChanges()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);

        await host.StorageCatalog.UpdateAsync(Block, new UpdateStorageOfferingCommand { MonthlyPrice = 999m, AnnualPrice = 9000m }, "platform");
        await host.SaveAsync();
        host.Clock.Advance(TimeSpan.FromDays(26));

        (await host.Subscriptions.GetPaymentDueAsync(host.SubscriptionOf(owner)))!.AddOnsAmount.Should().Be(400m);
    }

    // ── Reducing ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reduction_TakesEffectAtThePeriodEnd_NotBefore()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 3);
        var addOn = host.Uow.Context.StorageAddOns.Single();

        await host.Storage.RequestReductionAsync(owner, addOn.Id, 1, "owner");
        await host.SaveAsync();

        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + 3 * BlockMb, "the capacity is paid for until the period ends");
        var summary = await host.Storage.GetSummaryAsync(owner);
        summary.ProjectedLimitMb.Should().Be(PayrollBaseMb + BlockMb);

        host.Clock.Advance(TimeSpan.FromDays(26));
        (await host.Subscriptions.GetPaymentDueAsync(host.SubscriptionOf(owner)))!.AddOnsAmount
            .Should().Be(400m, "the renewal is already for the reduced blocks");

        host.Clock.UtcNow = host.SubscriptionOf(owner).CurrentPeriodEndUtc!.Value.AddMinutes(1);
        (await LimitAsync(host, tenantId)).Should().Be(PayrollBaseMb + BlockMb, "entitlements follow the date");
    }

    [Fact]
    public async Task Reduction_IsMadePermanent_ByTheLifecycleJob()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 3);
        var subscriptionId = host.SubscriptionOf(owner).Id;
        await host.Storage.RequestReductionAsync(owner, host.Uow.Context.StorageAddOns.Single().Id, 1, "owner");
        await host.SaveAsync();

        host.Clock.UtcNow = host.SubscriptionOf(owner).CurrentPeriodEndUtc!.Value.AddMinutes(1);
        await host.Lifecycle.ProcessAsync(subscriptionId, CancellationToken.None);

        host.Uow.Context.StorageAddOns.Single().Blocks.Should().Be(1);
        host.Uow.Context.StorageAddOns.Single().PendingBlocks.Should().BeNull();
        host.Uow.Context.BillingEvents.Should().Contain(e => e.Type == BillingEventTypes.StorageReductionApplied);
    }

    [Fact]
    public async Task ReducingToZero_RemovesTheHolding()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        var subscriptionId = host.SubscriptionOf(owner).Id;
        await host.Storage.RequestReductionAsync(owner, host.Uow.Context.StorageAddOns.Single().Id, 0, "owner");
        await host.SaveAsync();

        host.Clock.UtcNow = host.SubscriptionOf(owner).CurrentPeriodEndUtc!.Value.AddMinutes(1);
        await host.Lifecycle.ProcessAsync(subscriptionId, CancellationToken.None);

        host.Uow.Context.StorageAddOns.Should().BeEmpty();
        (await LimitAsync(host, tenantId)).Should().BeLessOrEqualTo(PayrollBaseMb);
    }

    [Fact]
    public async Task Reduction_ToTheSameOrMoreBlocks_IsRefused_AndCanBeWithdrawn()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 2);
        var id = host.Uow.Context.StorageAddOns.Single().Id;

        var same = () => host.Storage.RequestReductionAsync(owner, id, 2, "owner");
        await same.Should().ThrowAsync<SubscriptionRuleException>();

        await host.Storage.RequestReductionAsync(owner, id, 1, "owner");
        await host.SaveAsync();
        await host.Storage.WithdrawReductionAsync(owner, id, "owner");
        await host.SaveAsync();
        host.Uow.Context.StorageAddOns.Single().PendingBlocks.Should().BeNull();
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
        summary.PlanPrice.Should().Be(4999m);
        summary.AddOnsTotal.Should().Be(400m);
        summary.CanPurchase.Should().BeTrue();
        summary.Offerings.Should().ContainSingle(o => o.Code == Block);
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
        summary.PlanPrice.Should().Be(49990m);
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
