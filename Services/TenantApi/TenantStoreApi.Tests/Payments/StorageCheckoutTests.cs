using FluentAssertions;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Payments;

/// <summary>Buying storage blocks online: the page is for the quoted blocks, and paying it adds exactly those to the account's pool.</summary>
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

    [Fact]
    public async Task Start_OpensAPageForTheQuotedBlocks()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(15.5));

        var started = await host.Checkouts.StartStorageAsync(owner, Block, 2, owner);

        var request = host.Gateway.Created.Single();
        request.Amount.Should().Be(400m);
        request.ItemName.Should().Be("2 x 50 GB storage block");
        request.SuccessUrl.Should().Be($"https://hris.test/billing/payment?checkout={started.CheckoutId}");
        var checkout = host.Uow.Context.PaymentCheckouts.Single();
        checkout.Kind.Should().Be(PaymentCheckoutKind.StorageAddOn);
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
    public async Task Start_AtTheVeryEndOfThePeriod_NothingToPayOnline_IsRefused()
    {
        var (host, owner, _) = await PaidAccountAsync();
        host.Clock.UtcNow = host.SubscriptionOf(owner).CurrentPeriodEndUtc!.Value.AddSeconds(-1);

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
        host.Gateway.Pay("cs_1", 400m);

        var view = await host.Checkouts.RefreshAsync(owner, started.CheckoutId);

        view.Status.Should().Be(PaymentCheckoutStatus.Paid);
        view.Kind.Should().Be(PaymentCheckoutKind.StorageAddOn);
        view.PaymentMethod.Should().Be(PaymentMethod.GCash);
        view.PlanName.Should().Be("2 x 50 GB storage block");
        host.Uow.Context.StorageAddOns.Should().ContainSingle(a => a.Blocks == 2);
        host.Uow.Context.StorageAddOnPayments.Should().ContainSingle(p => p.Amount == 400m);
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
        var session = host.Gateway.Pay("cs_1", 200m);
        var body = host.Gateway.Deliver(session);

        var first = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);
        var second = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);

        first.Should().Be(WebhookOutcome.Settled);
        second.Should().Be(WebhookOutcome.Duplicate);
        host.Uow.Context.StorageAddOns.Single().Blocks.Should().Be(1);
    }
}
