using FluentAssertions;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Payments;

/// <summary>Buying storage blocks online: the page is for the quoted blocks, and paying it adds exactly those to the
/// account's pool. Renewing storage online: its own page, on its own cycle, apart from the plan's.</summary>
public class StorageCheckoutTests
{
    private const string Block = "storage-50gb";

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

    private static async Task BuyAsync(SubscriptionTestHost host, Guid owner, int blocks, BillingCycle? cycle = null)
    {
        var quote = await host.Storage.QuoteAsync(owner, Block, blocks, cycle);
        await host.Storage.ActivateAsync(owner, Block, blocks, quote.AmountDue, PaymentMethod.GCash, "pay_buy", "owner", cycle);
        await host.SaveAsync();
    }

    private static StorageSubscription StorageOf(SubscriptionTestHost host, Guid owner)
    {
        var subscriptionId = host.SubscriptionOf(owner).Id;
        return host.Uow.Context.StorageSubscriptions.Single(s => s.SubscriptionId == subscriptionId);
    }

    [Fact]
    public async Task Start_OpensAPageForTheQuotedBlocks()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(15.5));

        var started = await host.Checkouts.StartStorageAsync(owner, Block, 2, owner);

        var request = host.Gateway.Created.Single();
        request.Amount.Should().Be(800m, "the first blocks start storage's own month, at the full price");
        request.ItemName.Should().Be("2 x 50 GB storage block");
        request.SuccessUrl.Should().Be($"https://hris.test/billing/payment?checkout={started.CheckoutId}");
        var checkout = host.Uow.Context.PaymentCheckouts.Single();
        checkout.Kind.Should().Be(PaymentCheckoutKind.StorageAddOn);
        checkout.BillingCycle.Should().Be(BillingCycle.Monthly);
        checkout.StorageBlocks.Should().Be(2);
        checkout.CheckoutStatus.Should().Be(PaymentCheckoutStatus.Open);
        host.Uow.Context.StorageAddOns.Should().BeEmpty("nothing is added until it is paid");
    }

    [Fact]
    public async Task Start_OnAnAnnualAccount_ChargesTheAnnualBlockPrice()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Annual, "owner");
        await host.SaveAsync();
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 49990m, Method = PaymentMethod.BankTransfer }, "platform");
        await host.SaveAsync();

        await host.Checkouts.StartStorageAsync(owner, Block, 1, owner);

        host.Gateway.Created.Single().Amount.Should().Be(4000m);
    }

    [Fact]
    public async Task Start_AnnualStorage_OnAMonthlyPlan_IsPaidForTheYear_AndSettlesAsAnnual()
    {
        var (host, owner, _) = await PaidAccountAsync();

        var started = await host.Checkouts.StartStorageAsync(owner, Block, 1, owner, BillingCycle.Annual);
        host.Gateway.Pay("cs_1", 4000m);
        await host.Checkouts.RefreshAsync(owner, started.CheckoutId);

        host.Gateway.Created.Single().Amount.Should().Be(4000m);
        StorageOf(host, owner).BillingCycle.Should().Be(BillingCycle.Annual);
        StorageOf(host, owner).CurrentPeriodEndUtc.Should().Be(host.Clock.UtcNow.AddYears(1));
        host.SubscriptionOf(owner).BillingCycle.Should().Be(BillingCycle.Monthly);
    }

    [Fact]
    public async Task Start_TwiceForTheSameQuote_ReturnsTheSamePage()
    {
        var (host, owner, _) = await PaidAccountAsync();

        var first = await host.Checkouts.StartStorageAsync(owner, Block, 1, owner);
        var second = await host.Checkouts.StartStorageAsync(owner, Block, 1, owner);

        second.Should().Be(first);
        host.Gateway.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task Start_ForAnotherQuantity_ClosesTheOldPage_ButNotAPlanPayment()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(26));
        var plan = await host.Checkouts.StartAsync(owner, owner);
        var one = await host.Checkouts.StartStorageAsync(owner, Block, 1, owner);

        var two = await host.Checkouts.StartStorageAsync(owner, Block, 2, owner);

        two.CheckoutId.Should().NotBe(one.CheckoutId);
        host.Uow.Context.PaymentCheckouts.Single(c => c.Id == one.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Expired);
        host.Uow.Context.PaymentCheckouts.Single(c => c.Id == plan.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Open);
        host.Uow.Context.PaymentCheckouts.Single(c => c.Id == two.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Open);
    }

    [Fact]
    public async Task Start_StartingAPlanPayment_LeavesTheStoragePageOpen()
    {
        var (host, owner, _) = await PaidAccountAsync();
        var storage = await host.Checkouts.StartStorageAsync(owner, Block, 1, owner);
        host.Clock.Advance(TimeSpan.FromDays(26));

        await host.Checkouts.StartAsync(owner, owner);

        host.Uow.Context.PaymentCheckouts.Single(c => c.Id == storage.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Open);
    }

    [Fact]
    public async Task Start_WhenOnlinePaymentIsOff_IsRefused()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Gateway.IsEnabled = false;

        var act = () => host.Checkouts.StartStorageAsync(owner, Block, 1, owner);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*Online payment*");
    }

    [Fact]
    public async Task Start_AtTheVeryEndOfTheStoragePeriod_NothingToPayOnline_IsRefused()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        host.Clock.UtcNow = StorageOf(host, owner).CurrentPeriodEndUtc!.Value.AddSeconds(-1);

        var act = () => host.Checkouts.StartStorageAsync(owner, Block, 1, owner);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*nothing to pay*");
    }

    [Fact]
    public async Task Paying_AddsTheBlocks_AndRaisesTheLimitForEveryCompany()
    {
        var (host, owner, tenantId) = await PaidAccountAsync();
        var second = host.AddCompany(owner, "Second");
        host.Clock.Advance(TimeSpan.FromDays(15.5));
        var started = await host.Checkouts.StartStorageAsync(owner, Block, 2, owner);
        host.Gateway.Pay("cs_1", 800m);

        var view = await host.Checkouts.RefreshAsync(owner, started.CheckoutId);

        view.Status.Should().Be(PaymentCheckoutStatus.Paid);
        view.Kind.Should().Be(PaymentCheckoutKind.StorageAddOn);
        view.PaymentMethod.Should().Be(PaymentMethod.GCash);
        view.PlanName.Should().Be("2 x 50 GB storage block");
        host.Uow.Context.StorageAddOns.Should().ContainSingle(a => a.Blocks == 2);
        host.Uow.Context.StorageAddOnPayments.Should().ContainSingle(p => p.Amount == 800m);
        foreach (var company in new[] { tenantId, second })
            (await host.Resolver.ResolveActualForTenantAsync(company)).LimitOf(EntitlementKeys.StorageMaxMb).Should().Be(20480 + 2 * 51200);
        host.Uow.Context.SubscriptionPayments.Should().ContainSingle("a storage payment never becomes a plan payment");
    }

    [Fact]
    public async Task APaymentThatIsNotTheQuotedAmount_IsFlagged_NotApplied()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(15.5));
        var started = await host.Checkouts.StartStorageAsync(owner, Block, 2, owner);
        host.Gateway.Pay("cs_1", 100m);

        await host.Checkouts.RefreshAsync(owner, started.CheckoutId);

        host.Uow.Context.StorageAddOns.Should().BeEmpty();
        host.Uow.Context.BillingEvents.Should().Contain(e => e.Type == BillingEventTypes.CheckoutAmountMismatch);
    }

    [Fact]
    public async Task TheWebhookDeliveredTwice_AppliesTheBlocksOnce()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(15.5));
        await host.Checkouts.StartStorageAsync(owner, Block, 1, owner);
        var session = host.Gateway.Pay("cs_1", 400m);
        var body = host.Gateway.Deliver(session);

        var first = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);
        var second = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);

        first.Should().Be(WebhookOutcome.Settled);
        second.Should().Be(WebhookOutcome.Duplicate);
        host.Uow.Context.StorageAddOns.Single().Blocks.Should().Be(1);
    }

    // ── Renewing storage online ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Renewal_OpensItsOwnPage_ForThePaidBlocks_OnTheStorageCycle()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 2, BillingCycle.Annual);
        host.Clock.UtcNow = StorageOf(host, owner).CurrentPeriodEndUtc!.Value.AddDays(-3);

        var started = await host.Checkouts.StartStorageRenewalAsync(owner, owner);

        var request = host.Gateway.Created.Single();
        request.Amount.Should().Be(8000m);
        request.ItemName.Should().Be("Extra storage (annual), 2 blocks");
        var checkout = host.Uow.Context.PaymentCheckouts.Single(c => c.Id == started.CheckoutId);
        checkout.Kind.Should().Be(PaymentCheckoutKind.StorageRenewal);
        checkout.BillingCycle.Should().Be(BillingCycle.Annual);
    }

    [Fact]
    public async Task Renewal_WhenNothingIsDue_IsRefused()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);

        var act = () => host.Checkouts.StartStorageRenewalAsync(owner, owner);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*no storage renewal*");
    }

    [Fact]
    public async Task PayingTheRenewal_ExtendsStorage_AndNeverThePlan()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        var end = StorageOf(host, owner).CurrentPeriodEndUtc!.Value;
        var planEnd = host.SubscriptionOf(owner).CurrentPeriodEndUtc;
        host.Clock.UtcNow = end.AddDays(-2);
        var started = await host.Checkouts.StartStorageRenewalAsync(owner, owner);
        host.Gateway.Pay("cs_1", 400m);

        var view = await host.Checkouts.RefreshAsync(owner, started.CheckoutId);

        view.Status.Should().Be(PaymentCheckoutStatus.Paid);
        view.Kind.Should().Be(PaymentCheckoutKind.StorageRenewal);
        view.PlanName.Should().Be("Storage renewal (monthly), 1 block");
        StorageOf(host, owner).CurrentPeriodEndUtc.Should().Be(end.AddMonths(1));
        host.SubscriptionOf(owner).CurrentPeriodEndUtc.Should().Be(planEnd);
        host.Uow.Context.StorageAddOnPayments.Should().ContainSingle(p => p.Kind == StorageAddOnPaymentKind.Renewal && p.Amount == 400m);
        host.Uow.Context.SubscriptionPayments.Should().ContainSingle("the plan's own payment only");
    }

    [Fact]
    public async Task ARenewalPage_AndAPlanPage_CanBeOpenAtOnce()
    {
        var (host, owner, _) = await PaidAccountAsync();
        await BuyAsync(host, owner, 1);
        host.Clock.UtcNow = StorageOf(host, owner).CurrentPeriodEndUtc!.Value.AddDays(-2);

        var renewal = await host.Checkouts.StartStorageRenewalAsync(owner, owner);
        var plan = await host.Checkouts.StartAsync(owner, owner);

        host.Uow.Context.PaymentCheckouts.Single(c => c.Id == renewal.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Open);
        host.Uow.Context.PaymentCheckouts.Single(c => c.Id == plan.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Open);
    }
}
