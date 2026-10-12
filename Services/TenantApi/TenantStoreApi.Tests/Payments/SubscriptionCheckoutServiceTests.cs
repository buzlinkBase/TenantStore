using FluentAssertions;
using System.Text.Json;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Payments;

/// <summary>Paying online: opening the gateway's page for what's due, and applying the payment once it's paid.</summary>
public class SubscriptionCheckoutServiceTests
{
    /// <summary>A trial account that chose Payroll (monthly, 4,999), waiting for payment.</summary>
    private static async Task<(SubscriptionTestHost Host, Guid Owner)> ChosePayrollAsync(BillingCycle cycle = BillingCycle.Monthly)
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", cycle, "owner");
        await host.SaveAsync();
        return (host, owner);
    }

    private static PaymentCheckout CheckoutOf(SubscriptionTestHost host, Guid id) =>
        host.Uow.Context.PaymentCheckouts.Single(c => c.Id == id);

    // ── Starting ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_OpensAPageForTheChosenPlan_AtItsPrice()
    {
        var (host, owner) = await ChosePayrollAsync();
        var actor = Guid.NewGuid();

        var started = await host.Checkouts.StartAsync(owner, actor);

        started.CheckoutUrl.Should().Be("https://pay.test/cs_1");
        var request = host.Gateway.Created.Single();
        request.Amount.Should().Be(4999m);
        request.Currency.Should().Be("PHP");
        request.ItemName.Should().Be("Payroll plan (monthly)");
        request.SuccessUrl.Should().Be($"https://hris.test/billing/payment?checkout={started.CheckoutId}");
        request.CancelUrl.Should().Be($"https://hris.test/billing/payment?checkout={started.CheckoutId}&canceled=1");
        request.Metadata[SubscriptionCheckoutService.MetadataKeys.CheckoutId].Should().Be(started.CheckoutId.ToString());
        request.Metadata[SubscriptionCheckoutService.MetadataKeys.OwnerUserId].Should().Be(owner.ToString());

        var checkout = CheckoutOf(host, started.CheckoutId);
        checkout.CheckoutStatus.Should().Be(PaymentCheckoutStatus.Open);
        checkout.ProviderSessionId.Should().Be("cs_1");
        JsonSerializer.Deserialize<List<PaymentLineItemSnapshot>>(checkout.LineItemsJson!)
            .Should().Equal(new PaymentLineItemSnapshot("Payroll plan (monthly)", 4999m));
        checkout.CreatedByUserId.Should().Be(actor);
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == BillingEventTypes.CheckoutStarted && e.OwnerUserId == owner);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Trialing, "nothing changes until it's paid");
    }

    [Fact]
    public async Task Start_TwiceForTheSameQuote_ReturnsTheSamePage()
    {
        var (host, owner) = await ChosePayrollAsync();

        var first = await host.Checkouts.StartAsync(owner, owner);
        var second = await host.Checkouts.StartAsync(owner, owner);

        second.Should().Be(first);
        host.Gateway.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task Start_WithRecurringStorage_AddsSeparatePlanAndStorageLines()
    {
        var (host, owner) = await ChosePayrollAsync();
        var initialPayment = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand
        {
            Amount = 4999m,
            Method = PaymentMethod.BankTransfer,
        }, "platform");
        JsonSerializer.Deserialize<List<PaymentLineItemSnapshot>>(initialPayment.LineItemsJson!)
            .Should().Equal(new PaymentLineItemSnapshot("Payroll plan (monthly)", 4999m));
        var quote = await host.Storage.QuoteAsync(owner, "storage-50gb", 2);
        await host.Storage.ActivateAsync(owner, "storage-50gb", 2, quote.AmountDue,
            PaymentMethod.GCash, "pay_storage", "owner");
        await host.SaveAsync();
        host.Clock.Advance(TimeSpan.FromDays(26));

        var started = await host.Checkouts.StartAsync(owner, owner);

        var request = host.Gateway.Created.Single();
        request.Amount.Should().Be(5799m);
        request.LineItems.Should().Equal(
            new GatewayCheckoutLineItem("Payroll plan (monthly)", 4999m),
            new GatewayCheckoutLineItem(
                "Storage add-ons", 800m, "Storage add-ons for the next monthly billing period"));

        var checkout = CheckoutOf(host, started.CheckoutId);
        var checkoutLines = JsonSerializer.Deserialize<List<PaymentLineItemSnapshot>>(checkout.LineItemsJson!);
        checkoutLines.Should().Equal(
            new PaymentLineItemSnapshot("Payroll plan (monthly)", 4999m),
            new PaymentLineItemSnapshot(
                "Storage add-ons", 800m, "Storage add-ons for the next monthly billing period"));

        await host.Checkouts.HandleWebhookAsync(
            host.Gateway.Deliver(host.Gateway.Pay("cs_1", 5799m)),
            FakePaymentGateway.ValidSignature);

        var payment = host.Uow.Context.SubscriptionPayments.Single(item => item.Reference == "pay_cs_1");
        var paymentLines = JsonSerializer.Deserialize<List<PaymentLineItemSnapshot>>(payment.LineItemsJson!);
        paymentLines.Should().Equal(checkoutLines);
        var billing = await host.Views.GetBillingForAccountAsync(owner);
        billing.Payments.Single(item => item.Id == payment.Id)
            .LineItems.Should().Equal(checkoutLines);
    }

    [Fact]
    public async Task Start_AfterTheReuseWindow_OpensANewPage_AndClosesTheOld()
    {
        var (host, owner) = await ChosePayrollAsync();
        var first = await host.Checkouts.StartAsync(owner, owner);
        host.Clock.Advance(SubscriptionCheckoutService.ReuseWindow + TimeSpan.FromMinutes(1));

        var second = await host.Checkouts.StartAsync(owner, owner);

        second.CheckoutId.Should().NotBe(first.CheckoutId);
        host.Gateway.Expired.Should().Equal("cs_1");
        CheckoutOf(host, first.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Expired);
    }

    [Fact]
    public async Task Start_ExpiresAnOpenCheckoutCreatedBeforeTheCurrentCheckoutFormat()
    {
        var (host, owner) = await ChosePayrollAsync();
        var first = await host.Checkouts.StartAsync(owner, owner);
        var oldSession = host.Gateway.Sessions["cs_1"];
        host.Gateway.Sessions["cs_1"] = oldSession with
        {
            Metadata = new Dictionary<string, string>
            {
                [SubscriptionCheckoutService.MetadataKeys.CheckoutId] = first.CheckoutId.ToString(),
            },
        };

        var second = await host.Checkouts.StartAsync(owner, owner);

        second.CheckoutId.Should().NotBe(first.CheckoutId);
        host.Gateway.Expired.Should().Equal("cs_1");
        host.Gateway.Created.Should().HaveCount(2);
        host.Gateway.Created[1].Metadata[SubscriptionCheckoutService.MetadataKeys.CheckoutFormat]
            .Should().Be(SubscriptionCheckoutService.MetadataKeys.CurrentCheckoutFormat);
    }

    [Fact]
    public async Task Start_AfterChoosingAnotherCycle_ClosesThePageForTheOldQuote()
    {
        var (host, owner) = await ChosePayrollAsync();
        var monthly = await host.Checkouts.StartAsync(owner, owner);
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Annual, "owner");
        await host.SaveAsync();

        var annual = await host.Checkouts.StartAsync(owner, owner);

        host.Gateway.Created.Select(r => r.Amount).Should().Equal(4999m, 49990m);
        host.Gateway.Expired.Should().Equal("cs_1");
        CheckoutOf(host, monthly.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Expired);
        CheckoutOf(host, annual.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Open);
    }

    [Fact]
    public async Task Start_WithNothingDue_IsRefused()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        var act = () => host.Checkouts.StartAsync(owner, owner);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*nothing to pay*");
        host.Gateway.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_WhenOnlinePaymentIsOff_IsRefused()
    {
        var (host, owner) = await ChosePayrollAsync();
        host.Gateway.IsEnabled = false;

        var act = () => host.Checkouts.StartAsync(owner, owner);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
        host.Uow.Context.PaymentCheckouts.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_TheGatewayRefuses_IsRecordedAsFailed_AndThrows()
    {
        var (host, owner) = await ChosePayrollAsync();
        host.Gateway.FailNextCreate = true;

        var act = () => host.Checkouts.StartAsync(owner, owner);

        await act.Should().ThrowAsync<PaymentGatewayException>();
        var checkout = host.Uow.Context.PaymentCheckouts.Single();
        checkout.CheckoutStatus.Should().Be(PaymentCheckoutStatus.Failed);
        checkout.FailureReason.Should().Contain("not activated");
    }

    // ── Webhook ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PaidWebhook_ActivatesTheChosenPlan_AndRecordsThePayment()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.Checkouts.StartAsync(owner, owner);
        var body = host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m));

        var outcome = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);

        outcome.Should().Be(WebhookOutcome.Settled);
        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Active);
        subscription.PlanId.Should().Be(host.Uow.Context.Plans.Single(p => p.Code == "payroll").Id);
        subscription.CurrentPeriodEndUtc.Should().Be(host.Clock.UtcNow.AddMonths(1));

        var payment = host.Uow.Context.SubscriptionPayments.Single();
        payment.Amount.Should().Be(4999m);
        payment.Method.Should().Be(PaymentMethod.GCash);
        payment.Reference.Should().Be("pay_cs_1");
        payment.RecordedBy.Should().Be("fake");
        JsonSerializer.Deserialize<List<PaymentLineItemSnapshot>>(payment.LineItemsJson!)
            .Should().Equal(new PaymentLineItemSnapshot("Payroll plan (monthly)", 4999m));

        var checkout = CheckoutOf(host, started.CheckoutId);
        checkout.CheckoutStatus.Should().Be(PaymentCheckoutStatus.Paid);
        checkout.SubscriptionPaymentId.Should().Be(payment.Id);
        checkout.ProviderPaymentMethod.Should().Be("gcash");
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == BillingEventTypes.CheckoutPaid);
    }

    [Fact]
    public async Task PaidWebhook_DeliveredAgain_OrAsAnotherEvent_IsAppliedOnce()
    {
        var (host, owner) = await ChosePayrollAsync();
        await host.Checkouts.StartAsync(owner, owner);
        var session = host.Gateway.Pay("cs_1", 4999m);
        var first = host.Gateway.Deliver(session, "evt_1");
        var other = host.Gateway.Deliver(session, "evt_2");

        await host.Checkouts.HandleWebhookAsync(first, FakePaymentGateway.ValidSignature);
        var again = await host.Checkouts.HandleWebhookAsync(first, FakePaymentGateway.ValidSignature);
        var another = await host.Checkouts.HandleWebhookAsync(other, FakePaymentGateway.ValidSignature);

        again.Should().Be(WebhookOutcome.Duplicate);
        another.Should().Be(WebhookOutcome.Duplicate);
        host.Uow.Context.SubscriptionPayments.Should().ContainSingle();
        host.SubscriptionOf(owner).CurrentPeriodEndUtc.Should().Be(host.Clock.UtcNow.AddMonths(1));
    }

    [Theory]
    [InlineData(GatewayEventKind.PaymentRefunded, BillingEventTypes.PaymentRefunded)]
    [InlineData(GatewayEventKind.PaymentDisputed, BillingEventTypes.PaymentDisputed)]
    public async Task RefundOrDispute_OfAPaidCheckout_IsFlaggedOnce_AndChangesNothing(GatewayEventKind kind, string eventType)
    {
        var (host, owner) = await ChosePayrollAsync();
        await host.Checkouts.StartAsync(owner, owner);
        await host.Checkouts.HandleWebhookAsync(host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m)), FakePaymentGateway.ValidSignature);
        var body = host.Gateway.DeliverIssue("pay_cs_1", kind, 4999m);

        var outcome = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);
        var again = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);

        outcome.Should().Be(WebhookOutcome.Flagged);
        again.Should().Be(WebhookOutcome.Duplicate);
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == eventType && e.OwnerUserId == owner);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active, "an operator decides what follows");
        host.Uow.Context.SubscriptionPayments.Single().VoidedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Refund_OfAPaymentWeNeverTook_IsFlaggedWithoutAnOwner()
    {
        var host = new SubscriptionTestHost();
        var body = host.Gateway.DeliverIssue("pay_stranger", GatewayEventKind.PaymentRefunded, 100m);

        var outcome = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);

        outcome.Should().Be(WebhookOutcome.Flagged);
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == BillingEventTypes.PaymentRefunded && e.OwnerUserId == null);
    }

    // ── Reconciliation (a webhook that never arrived) ────────────────────────────────────────

    [Fact]
    public async Task Reconcile_AnUnpaidCheckoutTheGatewayCallsPaid_IsSettledOnce()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.Checkouts.StartAsync(owner, owner);
        host.Gateway.Pay("cs_1", 4999m);

        var first = await host.Checkouts.ReconcileAsync(started.CheckoutId);
        var second = await host.Checkouts.ReconcileAsync(started.CheckoutId);

        first.Should().Be(ReconcileResult.Settled);
        second.Should().Be(ReconcileResult.Unchanged);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active);
        host.Uow.Context.SubscriptionPayments.Should().ContainSingle();
    }

    [Fact]
    public async Task Reconcile_APaidCheckoutTheGatewayCallsRefunded_IsFlaggedOnce()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.Checkouts.StartAsync(owner, owner);
        await host.Checkouts.HandleWebhookAsync(host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m)), FakePaymentGateway.ValidSignature);
        host.Gateway.Refunds["pay_cs_1"] = 4999m;

        var first = await host.Checkouts.ReconcileAsync(started.CheckoutId);
        var second = await host.Checkouts.ReconcileAsync(started.CheckoutId);

        first.Should().Be(ReconcileResult.RefundFlagged);
        second.Should().Be(ReconcileResult.Unchanged);
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == BillingEventTypes.PaymentRefunded && e.OwnerUserId == owner);
    }

    [Fact]
    public async Task Reconcile_ARefundTheWebhookAlreadyRecorded_IsNotRecordedAgain()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.Checkouts.StartAsync(owner, owner);
        await host.Checkouts.HandleWebhookAsync(host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m)), FakePaymentGateway.ValidSignature);
        await host.Checkouts.HandleWebhookAsync(
            host.Gateway.DeliverIssue("pay_cs_1", GatewayEventKind.PaymentRefunded, 4999m), FakePaymentGateway.ValidSignature);
        host.Gateway.Refunds["pay_cs_1"] = 4999m;

        var result = await host.Checkouts.ReconcileAsync(started.CheckoutId);

        result.Should().Be(ReconcileResult.Unchanged);
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == BillingEventTypes.PaymentRefunded);
    }

    [Fact]
    public async Task Reconcile_WhenTheGatewayCantBeReached_ChangesNothing_AndDoesNotThrow()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.Checkouts.StartAsync(owner, owner);
        await host.Checkouts.HandleWebhookAsync(host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m)), FakePaymentGateway.ValidSignature);
        host.Gateway.FailPaymentLookup = true;

        var result = await host.Checkouts.ReconcileAsync(started.CheckoutId);

        result.Should().Be(ReconcileResult.Unchanged);
    }

    [Fact]
    public async Task ListReconcilable_IncludesRecentUnpaidAndPaidCheckouts_NotOldUnpaidOnes()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.Checkouts.StartAsync(owner, owner);

        (await host.Checkouts.ListReconcilableAsync(null)).Should().Equal(started.CheckoutId);
        (await host.Checkouts.ListReconcilableAsync(Guid.NewGuid())).Should().BeEmpty("another owner's checkouts aren't asked about");

        host.Clock.Advance(SubscriptionCheckoutService.ReconcileOpenWindow + TimeSpan.FromHours(1));
        (await host.Checkouts.ListReconcilableAsync(null)).Should().BeEmpty();
    }

    [Fact]
    public async Task Webhook_WithoutAValidSignature_IsRejected()
    {
        var (host, owner) = await ChosePayrollAsync();
        await host.Checkouts.StartAsync(owner, owner);
        var body = host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m));

        var outcome = await host.Checkouts.HandleWebhookAsync(body, "forged");

        outcome.Should().Be(WebhookOutcome.Rejected);
        host.Uow.Context.SubscriptionPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task Webhook_FromTheOtherMode_IsIgnored()
    {
        var (host, owner) = await ChosePayrollAsync();
        await host.Checkouts.StartAsync(owner, owner);
        var body = host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m), liveMode: true);

        var outcome = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);

        outcome.Should().Be(WebhookOutcome.Ignored);
        host.Uow.Context.SubscriptionPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task Webhook_ForASessionWeNeverOpened_IsFlagged_NotApplied()
    {
        var host = new SubscriptionTestHost();
        var stranger = new GatewaySessionState(
            "cs_unknown",
            new GatewayPayment("pay_x", 4999m, "PHP", PaymentMethod.Card, "card", null),
            new Dictionary<string, string>(),
            LiveMode: false);
        var body = host.Gateway.Deliver(stranger);

        var outcome = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);
        var retried = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);

        outcome.Should().Be(WebhookOutcome.Flagged);
        retried.Should().Be(WebhookOutcome.Duplicate);
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == BillingEventTypes.CheckoutUnmatched);
        host.Uow.Context.SubscriptionPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task Webhook_PaidADifferentAmount_IsFlagged_NotApplied()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.Checkouts.StartAsync(owner, owner);
        var body = host.Gateway.Deliver(host.Gateway.Pay("cs_1", 1m));

        var outcome = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);

        outcome.Should().Be(WebhookOutcome.Flagged);
        host.Uow.Context.BillingEvents.Should().ContainSingle(e => e.Type == BillingEventTypes.CheckoutAmountMismatch && e.OwnerUserId == owner);
        host.Uow.Context.SubscriptionPayments.Should().BeEmpty();
        CheckoutOf(host, started.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Open);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Trialing);
    }

    [Fact]
    public async Task PayingAClosedPage_StillAppliesWhatWasPaidFor()
    {
        var (host, owner) = await ChosePayrollAsync();
        var monthly = await host.Checkouts.StartAsync(owner, owner);
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Annual, "owner");
        await host.SaveAsync();
        await host.Checkouts.StartAsync(owner, owner);
        var body = host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m));

        var outcome = await host.Checkouts.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);

        outcome.Should().Be(WebhookOutcome.Settled);
        CheckoutOf(host, monthly.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Paid);
        var subscription = host.SubscriptionOf(owner);
        subscription.BillingCycle.Should().Be(BillingCycle.Monthly, "the money paid was for a month");
        subscription.CurrentPeriodEndUtc.Should().Be(host.Clock.UtcNow.AddMonths(1));
    }

    [Fact]
    public async Task OtherEvents_AreIgnored()
    {
        var host = new SubscriptionTestHost();
        host.Gateway.Deliveries["evt_other"] = new GatewayWebhookEvent("evt_other", "payment.failed", GatewayEventKind.Other, false, null);

        (await host.Checkouts.HandleWebhookAsync("evt_other", FakePaymentGateway.ValidSignature)).Should().Be(WebhookOutcome.Ignored);
    }

    // ── Return screen ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_AsksTheGateway_AndSettlesAPaidCheckout_WithoutTheWebhook()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.Checkouts.StartAsync(owner, owner);
        host.Gateway.Pay("cs_1", 4999m, "card", PaymentMethod.Card);

        var view = await host.Checkouts.RefreshAsync(owner, started.CheckoutId);

        view.Status.Should().Be(PaymentCheckoutStatus.Paid);
        view.PlanName.Should().Be("Payroll");
        view.PaymentMethod.Should().Be(PaymentMethod.Card);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active);

        // ... and the webhook arriving afterwards changes nothing.
        var late = await host.Checkouts.HandleWebhookAsync(host.Gateway.Deliver(host.Gateway.Sessions["cs_1"]), FakePaymentGateway.ValidSignature);
        late.Should().Be(WebhookOutcome.Duplicate);
        host.Uow.Context.SubscriptionPayments.Should().ContainSingle();
    }

    [Fact]
    public async Task Refresh_WhileUnpaid_StaysOpen()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.Checkouts.StartAsync(owner, owner);

        var view = await host.Checkouts.RefreshAsync(owner, started.CheckoutId);

        view.Status.Should().Be(PaymentCheckoutStatus.Open);
        view.Amount.Should().Be(4999m);
        view.PaymentMethod.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_OfAnotherAccountsCheckout_IsNotFound()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.Checkouts.StartAsync(owner, owner);

        var act = () => host.Checkouts.RefreshAsync(Guid.NewGuid(), started.CheckoutId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
