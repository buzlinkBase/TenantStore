using FluentAssertions;
using System.Text.Json;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Payments;

/// <summary>
/// Auto renew through the gateway's recurring billing: turned on by paying what's due (which saves the
/// card or Maya account), each draft invoice corrected to our price, each paid invoice recorded as an
/// ordinary renewal, failures left to the gateway's retries and our own grace, and anything that would
/// make the gateway's schedule wrong turning it off. Plan and storage are independent.
/// </summary>
public class AutoRenewTests
{
    private const string Block = "storage-50gb";

    /// <summary>A trial account that chose Payroll (monthly, 4,999), waiting for payment.</summary>
    private static async Task<(SubscriptionTestHost Host, Guid Owner)> ChosePayrollAsync()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");
        await host.SaveAsync();
        return (host, owner);
    }

    /// <summary>Turns auto renew on for <paramref name="command"/>'s target: start, pay by card, the first invoice's paid webhook.</summary>
    private static async Task<(AutoRenewStarted Started, string GatewaySubscription)> TurnOnAsync(
        SubscriptionTestHost host, Guid owner, StartAutoRenewCommand? command = null)
    {
        var started = await host.AutoRenew.StartAsync(owner, owner, command ?? new StartAutoRenewCommand { Target = AutoRenewTarget.Plan });
        await host.AutoRenew.ConfirmAsync(owner, started.CheckoutId, new ConfirmAutoRenewCommand { Method = "card", PaymentMethodId = "pm_card_1" });
        var checkout = host.Uow.Context.PaymentCheckouts.Single(c => c.Id == started.CheckoutId);
        host.Gateway.PayIntent(checkout.ProviderIntentId!, started.Amount);
        var gatewaySubscription = checkout.ProviderSessionId!;
        var first = host.Gateway.BillingSubscriptions[gatewaySubscription];
        var body = host.Gateway.DeliverInvoice(GatewayEventKind.SubscriptionInvoicePaid,
            new GatewayInvoice(first.LatestInvoiceId!, gatewaySubscription, started.Amount, "PHP", "paid", null, first.PaymentIntentId));
        (await host.AutoRenew.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature)).Should().Be(WebhookOutcome.Settled);
        return (started, gatewaySubscription);
    }

    private static AutoRenewMandate MandateOf(SubscriptionTestHost host, AutoRenewTarget target = AutoRenewTarget.Plan) =>
        host.Uow.Context.AutoRenewMandates.Where(m => m.Target == target).ToList().OrderBy(m => m.Id).Last();

    private static int Emails(SubscriptionTestHost host, string template) =>
        host.Publisher.Invocations.SelectMany(i => i.Arguments.OfType<SubscriptionReminderEmail>()).Count(e => e.Template == template);

    /// <summary>A later cycle, paid: the gateway raised <paramref name="invoiceId"/>, charged it, and says so.</summary>
    private static async Task<WebhookOutcome> PayRenewalAsync(SubscriptionTestHost host, string gatewaySubscription, string invoiceId, decimal amount)
    {
        var invoice = host.Gateway.RaiseInvoice(gatewaySubscription, invoiceId, amount, "paid");
        host.Gateway.PayIntent(invoice.PaymentIntentId!, amount);
        return await host.AutoRenew.HandleWebhookAsync(
            host.Gateway.DeliverInvoice(GatewayEventKind.SubscriptionInvoicePaid, invoice), FakePaymentGateway.ValidSignature);
    }

    // ── Turning it on ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_CreatesTheGatewaySubscription_PricedAtWhatsDue_AndAnOpenCheckout()
    {
        var (host, owner) = await ChosePayrollAsync();

        var started = await host.AutoRenew.StartAsync(owner, owner, new StartAutoRenewCommand { Target = AutoRenewTarget.Plan });

        started.Amount.Should().Be(4999m);
        started.BillingCycle.Should().Be(BillingCycle.Monthly);
        started.PublicKey.Should().Be("pk_test_fake");
        host.Gateway.Customers.Should().ContainSingle(c => c.Email == "owner@acme.ph");
        host.Gateway.Plans.Values.Should().ContainSingle(p => p.Amount == 4999m && p.Cycle == BillingCycle.Monthly);
        var checkout = host.Uow.Context.PaymentCheckouts.Single(c => c.Id == started.CheckoutId);
        checkout.MandateId.Should().NotBeNull();
        checkout.Kind.Should().Be(PaymentCheckoutKind.Plan);
        checkout.ProviderIntentId.Should().Be("pi_1");
        checkout.CheckoutStatus.Should().Be(PaymentCheckoutStatus.Open);
        MandateOf(host).Status.Should().Be(AutoRenewMandateStatus.Pending);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Trialing, "nothing changes until it's paid");
    }

    [Fact]
    public async Task Start_WithNothingDue_IsRefused()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();

        var act = () => host.AutoRenew.StartAsync(owner, owner, new StartAutoRenewCommand { Target = AutoRenewTarget.Plan });

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*nothing to pay*");
    }

    [Fact]
    public async Task Confirm_ByCard_SendsThePayerToAuthorizeIt_AndTakesOnlyAGatewayToken()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.AutoRenew.StartAsync(owner, owner, new StartAutoRenewCommand { Target = AutoRenewTarget.Plan });

        var rawCard = () => host.AutoRenew.ConfirmAsync(owner, started.CheckoutId,
            new ConfirmAutoRenewCommand { Method = "card", PaymentMethodId = "4343434343434345" });
        await rawCard.Should().ThrowAsync<SubscriptionRuleException>();

        var confirmed = await host.AutoRenew.ConfirmAsync(owner, started.CheckoutId,
            new ConfirmAutoRenewCommand { Method = "card", PaymentMethodId = "pm_card_1" });

        confirmed.RedirectUrl.Should().Be("https://pay.test/authorize/pi_1");
        host.Gateway.Attached.Should().ContainSingle(a => a.PaymentMethodId == "pm_card_1"
            && a.ReturnUrl == $"https://hris.test/billing/payment?checkout={started.CheckoutId}");
    }

    [Fact]
    public async Task Confirm_ByMaya_CreatesTheWalletMethodOnTheServer()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.AutoRenew.StartAsync(owner, owner, new StartAutoRenewCommand { Target = AutoRenewTarget.Plan });

        await host.AutoRenew.ConfirmAsync(owner, started.CheckoutId, new ConfirmAutoRenewCommand { Method = "maya" });

        host.Gateway.WalletMethods.Should().Equal("paymaya");
        host.Gateway.Attached.Single().PaymentMethodId.Should().Be("pm_wallet_1");
    }

    [Fact]
    public async Task PayingTheFirstInvoice_ActivatesThePlan_AndTurnsAutoRenewOn()
    {
        var (host, owner) = await ChosePayrollAsync();

        await TurnOnAsync(host, owner);

        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Active);
        subscription.CurrentPeriodEndUtc.Should().Be(SubscriptionTestHost.Start.AddMonths(1));
        var mandate = MandateOf(host);
        mandate.Status.Should().Be(AutoRenewMandateStatus.Active);
        mandate.PaymentMethodLabel.Should().Be("Visa •••• 4242");
        host.Uow.Context.SubscriptionPayments.Should().ContainSingle(p => p.Amount == 4999m && p.Method == PaymentMethod.Card);
        host.Uow.Context.PaymentCheckouts.Single(c => c.MandateId == mandate.Id).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Paid);
        host.Uow.Context.BillingEvents.Should().Contain(e => e.Type == BillingEventTypes.AutoRenewActivated);
        Emails(host, SubscriptionEmailTemplates.AutoRenewOn).Should().Be(1);
    }

    [Fact]
    public async Task TheReturnScreen_SettlesTheFirstPayment_WhenTheWebhookIsLate()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.AutoRenew.StartAsync(owner, owner, new StartAutoRenewCommand { Target = AutoRenewTarget.Plan });
        host.Gateway.PayIntent("pi_1", 4999m);

        var view = await host.AutoRenew.RefreshAsync(owner, started.CheckoutId);

        view.Status.Should().Be(PaymentCheckoutStatus.Paid);
        view.AutoRenew.Should().BeTrue();
        MandateOf(host).Status.Should().Be(AutoRenewMandateStatus.Active);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active);
    }

    [Fact]
    public async Task StartingAgain_ReplacesTheEarlierSetup()
    {
        var (host, owner) = await ChosePayrollAsync();
        var first = await host.AutoRenew.StartAsync(owner, owner, new StartAutoRenewCommand { Target = AutoRenewTarget.Plan });

        await host.AutoRenew.StartAsync(owner, owner, new StartAutoRenewCommand { Target = AutoRenewTarget.Plan });

        host.Gateway.CancelledSubscriptions.Should().Equal("subs_1");
        host.Uow.Context.PaymentCheckouts.Single(c => c.Id == first.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Expired);
        host.Uow.Context.AutoRenewMandates.Count(m => m.Status != AutoRenewMandateStatus.Canceled).Should().Be(1);
        host.Gateway.Customers.Should().ContainSingle("the gateway customer is reused");
    }

    // ── Each cycle ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ADraftInvoice_IsCorrectedToOurPrice()
    {
        var (host, owner) = await ChosePayrollAsync();
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner);
        await host.Subscriptions.AddDiscountAsync(owner, new AddDiscountCommand { Percent = 10 }, "platform");
        await host.SaveAsync();

        var draft = host.Gateway.RaiseInvoice(gatewaySubscription, "inv_1_2", 4999m);
        var outcome = await host.AutoRenew.HandleWebhookAsync(
            host.Gateway.DeliverInvoice(GatewayEventKind.SubscriptionInvoiceCreated, draft), FakePaymentGateway.ValidSignature);

        outcome.Should().Be(WebhookOutcome.Settled);
        host.Gateway.LineItems.Should().ContainSingle(l => l.InvoiceId == "inv_1_2" && l.Amount == -499.90m);
    }

    [Fact]
    public async Task ADraftInvoice_AtOurPrice_IsLeftAlone_AndReviewedOnce()
    {
        var (host, owner) = await ChosePayrollAsync();
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner);
        var draft = host.Gateway.RaiseInvoice(gatewaySubscription, "inv_1_2", 4999m);
        var body = host.Gateway.DeliverInvoice(GatewayEventKind.SubscriptionInvoiceCreated, draft);

        await host.AutoRenew.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature);
        (await host.AutoRenew.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature)).Should().Be(WebhookOutcome.Duplicate);

        host.Gateway.LineItems.Should().BeEmpty();
    }

    [Fact]
    public async Task ADraftInvoice_ForAPlanSetToCancel_StopsAutoRenew_InsteadOfCharging()
    {
        var (host, owner) = await ChosePayrollAsync();
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner);
        await host.Subscriptions.CancelAsync(owner, "owner");
        await host.SaveAsync();

        var draft = host.Gateway.RaiseInvoice(gatewaySubscription, "inv_1_2", 4999m);
        await host.AutoRenew.HandleWebhookAsync(
            host.Gateway.DeliverInvoice(GatewayEventKind.SubscriptionInvoiceCreated, draft), FakePaymentGateway.ValidSignature);

        host.Gateway.CancelledSubscriptions.Should().Contain(gatewaySubscription);
        host.Gateway.LineItems.Should().BeEmpty();
        MandateOf(host).Status.Should().Be(AutoRenewMandateStatus.Canceled);
    }

    [Fact]
    public async Task APaidRenewal_ExtendsThePeriod_AsAManualRenewalWould_Once()
    {
        var (host, owner) = await ChosePayrollAsync();
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner);
        var end = host.SubscriptionOf(owner).CurrentPeriodEndUtc!.Value;
        host.Clock.UtcNow = end.AddDays(-1);

        (await PayRenewalAsync(host, gatewaySubscription, "inv_1_2", 4999m)).Should().Be(WebhookOutcome.Settled);
        var again = await host.AutoRenew.HandleWebhookAsync($"evt_{GatewayEventKind.SubscriptionInvoicePaid}_inv_1_2", FakePaymentGateway.ValidSignature);

        again.Should().Be(WebhookOutcome.Duplicate);
        host.SubscriptionOf(owner).CurrentPeriodEndUtc.Should().Be(end.AddMonths(1));
        var renewal = host.Uow.Context.SubscriptionPayments.OrderBy(p => p.PeriodStartUtc).Last();
        renewal.RecordedBy.Should().Be(AutoRenewService.Actor);
        renewal.Reference.Should().Be("pay_pi_inv_1_2");
        renewal.PeriodStartUtc.Should().Be(end);
        host.Uow.Context.SubscriptionPayments.Count().Should().Be(2);
    }

    [Fact]
    public async Task AFailedCharge_TellsTheOwner_AndLeavesThePeriodToOurGrace()
    {
        var (host, owner) = await ChosePayrollAsync();
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner);
        var end = host.SubscriptionOf(owner).CurrentPeriodEndUtc;

        var invoice = host.Gateway.RaiseInvoice(gatewaySubscription, "inv_1_2", 4999m, "open");
        await host.AutoRenew.HandleWebhookAsync(
            host.Gateway.DeliverInvoice(GatewayEventKind.SubscriptionInvoiceFailed, invoice), FakePaymentGateway.ValidSignature);

        MandateOf(host).Status.Should().Be(AutoRenewMandateStatus.PastDue);
        MandateOf(host).LastFailureAtUtc.Should().NotBeNull();
        host.SubscriptionOf(owner).CurrentPeriodEndUtc.Should().Be(end);
        Emails(host, SubscriptionEmailTemplates.AutoRenewChargeFailed).Should().Be(1);
    }

    [Fact]
    public async Task RetriesRunningOut_TurnAutoRenewOff()
    {
        var (host, owner) = await ChosePayrollAsync();
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner);

        await host.AutoRenew.HandleWebhookAsync(host.Gateway.DeliverSubscription(gatewaySubscription, "unpaid"), FakePaymentGateway.ValidSignature);

        MandateOf(host).Status.Should().Be(AutoRenewMandateStatus.Canceled);
        host.Gateway.CancelledSubscriptions.Should().Contain(gatewaySubscription);
        Emails(host, SubscriptionEmailTemplates.AutoRenewStopped).Should().Be(1);
    }

    [Fact]
    public async Task OtherWebhooks_StillReachTheHostedCheckout()
    {
        var (host, owner) = await ChosePayrollAsync();
        await host.Checkouts.StartAsync(owner, owner);
        var body = host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m));

        (await host.AutoRenew.HandleWebhookAsync(body, FakePaymentGateway.ValidSignature)).Should().Be(WebhookOutcome.Settled);
        (await host.AutoRenew.HandleWebhookAsync(body, "forged")).Should().Be(WebhookOutcome.Rejected);
    }

    // ── Turning it off ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TurningItOff_StopsTheGateway_AndThePaidPeriodRunsOn()
    {
        var (host, owner) = await ChosePayrollAsync();
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner);

        await host.AutoRenew.TurnOffAsync(owner, AutoRenewTarget.Plan, owner.ToString(), "Turned off by the owner");

        host.Gateway.CancelledSubscriptions.Should().Equal(gatewaySubscription);
        MandateOf(host).Status.Should().Be(AutoRenewMandateStatus.Canceled);
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active);
        var off = () => host.AutoRenew.TurnOffAsync(owner, AutoRenewTarget.Plan, owner.ToString(), "again");
        await off.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task WhileAutoRenewIsOn_ARenewalCantAlsoBePaidByHand()
    {
        var (host, owner) = await ChosePayrollAsync();
        await TurnOnAsync(host, owner);
        host.Clock.UtcNow = host.SubscriptionOf(owner).CurrentPeriodEndUtc!.Value.AddDays(-3);

        var act = () => host.Checkouts.StartAsync(owner, owner);

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*renews automatically*");
    }

    [Fact]
    public async Task TheLifecycle_StopsAutoRenew_OnceThePlanIsSetToCancel()
    {
        var (host, owner) = await ChosePayrollAsync();
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner);
        await host.Subscriptions.CancelAsync(owner, "owner");
        await host.SaveAsync();

        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);

        host.Gateway.CancelledSubscriptions.Should().Contain(gatewaySubscription);
        MandateOf(host).Status.Should().Be(AutoRenewMandateStatus.Canceled);
    }

    [Fact]
    public async Task TheLifecycle_ClosesAFirstPaymentThatWasNeverMade()
    {
        var (host, owner) = await ChosePayrollAsync();
        var started = await host.AutoRenew.StartAsync(owner, owner, new StartAutoRenewCommand { Target = AutoRenewTarget.Plan });

        host.Clock.Advance(TimeSpan.FromHours(26));
        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);

        MandateOf(host).Status.Should().Be(AutoRenewMandateStatus.Canceled);
        host.Uow.Context.PaymentCheckouts.Single(c => c.Id == started.CheckoutId).CheckoutStatus.Should().Be(PaymentCheckoutStatus.Expired);
    }

    [Fact]
    public async Task TheLifecycle_RecordsACharge_WhoseWebhookNeverCame()
    {
        var (host, owner) = await ChosePayrollAsync();
        host.Gateway.NextBillingDateUtc = SubscriptionTestHost.Start.AddMonths(1).AddDays(-1);
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner);
        var end = host.SubscriptionOf(owner).CurrentPeriodEndUtc!.Value;

        var invoice = host.Gateway.RaiseInvoice(gatewaySubscription, "inv_1_2", 4999m, "paid");
        host.Gateway.PayIntent(invoice.PaymentIntentId!, 4999m);
        host.Clock.UtcNow = end.AddHours(-12);
        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);

        host.SubscriptionOf(owner).CurrentPeriodEndUtc.Should().Be(end.AddMonths(1));
    }

    // ── Storage, on its own ──────────────────────────────────────────────────────────────────

    private static async Task<(SubscriptionTestHost Host, Guid Owner)> PaidPayrollAsync()
    {
        var (host, owner) = await ChosePayrollAsync();
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999m, Method = PaymentMethod.BankTransfer }, "platform");
        await host.SaveAsync();
        return (host, owner);
    }

    [Fact]
    public async Task Storage_TurnedOnWithItsFirstPurchase_RenewsOnItsOwnCycle_AndLeavesThePlanAlone()
    {
        var (host, owner) = await PaidPayrollAsync();

        var (started, gatewaySubscription) = await TurnOnAsync(host, owner, new StartAutoRenewCommand
        {
            Target = AutoRenewTarget.Storage, OfferingCode = Block, Blocks = 2, BillingCycle = BillingCycle.Annual,
        });

        started.Amount.Should().Be(8000m);
        host.Gateway.Plans.Values.Should().ContainSingle(p => p.Cycle == BillingCycle.Annual && p.Amount == 8000m);
        var storage = host.Uow.Context.StorageSubscriptions.Single();
        storage.BillingCycle.Should().Be(BillingCycle.Annual);
        host.Uow.Context.StorageAddOns.Single().Blocks.Should().Be(2);
        MandateOf(host, AutoRenewTarget.Storage).Status.Should().Be(AutoRenewMandateStatus.Active);
        host.Uow.Context.AutoRenewMandates.Should().NotContain(m => m.Target == AutoRenewTarget.Plan);

        var end = storage.CurrentPeriodEndUtc!.Value;
        host.Clock.UtcNow = end.AddDays(-1);
        (await PayRenewalAsync(host, gatewaySubscription, "inv_1_2", 8000m)).Should().Be(WebhookOutcome.Settled);

        host.Uow.Context.StorageSubscriptions.Single().CurrentPeriodEndUtc.Should().Be(end.AddYears(1));
        host.Uow.Context.StorageAddOnPayments.Should().Contain(p => p.Kind == StorageAddOnPaymentKind.Renewal
            && p.Amount == 8000m && p.RecordedBy == AutoRenewService.Actor);
        host.Uow.Context.SubscriptionPayments.Should().ContainSingle("the plan was never charged by storage's auto renew");
    }

    [Fact]
    public async Task Storage_ADraftInvoice_PicksUpBlocksBoughtSince()
    {
        var (host, owner) = await PaidPayrollAsync();
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner, new StartAutoRenewCommand
        {
            Target = AutoRenewTarget.Storage, OfferingCode = Block, Blocks = 1, BillingCycle = BillingCycle.Monthly,
        });
        var quote = await host.Storage.QuoteAsync(owner, Block, 2);
        await host.Storage.ActivateAsync(owner, Block, 2, quote.AmountDue, PaymentMethod.GCash, "pay_more", "owner");
        await host.SaveAsync();

        var draft = host.Gateway.RaiseInvoice(gatewaySubscription, "inv_1_2", 400m);
        await host.AutoRenew.HandleWebhookAsync(
            host.Gateway.DeliverInvoice(GatewayEventKind.SubscriptionInvoiceCreated, draft), FakePaymentGateway.ValidSignature);

        host.Gateway.LineItems.Should().ContainSingle(l => l.Amount == 800m, "3 blocks at 400 now, invoiced for 1");
    }

    [Fact]
    public async Task Storage_ACycleSwitch_TurnsAutoRenewOff_RatherThanChargeByTheOldCycle()
    {
        var (host, owner) = await PaidPayrollAsync();
        var (_, gatewaySubscription) = await TurnOnAsync(host, owner, new StartAutoRenewCommand
        {
            Target = AutoRenewTarget.Storage, OfferingCode = Block, Blocks = 1, BillingCycle = BillingCycle.Monthly,
        });

        await host.StoragePeriod.RequestCycleChangeAsync(owner, BillingCycle.Annual, "owner");
        await host.SaveAsync();
        await host.Lifecycle.ProcessAsync(host.SubscriptionOf(owner).Id, CancellationToken.None);

        MandateOf(host, AutoRenewTarget.Storage).Status.Should().Be(AutoRenewMandateStatus.Canceled);
        MandateOf(host, AutoRenewTarget.Storage).CancelReason.Should().Contain("cycle");
        host.Gateway.CancelledSubscriptions.Should().Contain(gatewaySubscription);
        Emails(host, SubscriptionEmailTemplates.AutoRenewStopped).Should().Be(1);
    }

    [Fact]
    public async Task Storage_APurchaseProratedIntoARunningPeriod_CantStartAutoRenew()
    {
        var (host, owner) = await PaidPayrollAsync();
        var quote = await host.Storage.QuoteAsync(owner, Block, 1);
        await host.Storage.ActivateAsync(owner, Block, 1, quote.AmountDue, PaymentMethod.GCash, "pay_1", "owner");
        await host.SaveAsync();

        var act = () => host.AutoRenew.StartAsync(owner, owner, new StartAutoRenewCommand
        {
            Target = AutoRenewTarget.Storage, OfferingCode = Block, Blocks = 1,
        });

        await act.Should().ThrowAsync<SubscriptionRuleException>().WithMessage("*next storage renewal*");
    }

    [Fact]
    public async Task TheViews_ShowEachTargetsAutoRenew()
    {
        var (host, owner) = await PaidPayrollAsync();
        await TurnOnAsync(host, owner, new StartAutoRenewCommand
        {
            Target = AutoRenewTarget.Storage, OfferingCode = Block, Blocks = 1, BillingCycle = BillingCycle.Monthly,
        });

        var billing = await host.Views.GetBillingForAccountAsync(owner);
        var storage = await host.Storage.GetSummaryAsync(owner);

        billing.AutoRenew.Should().ContainSingle(a => a.Target == AutoRenewTarget.Storage && a.PaymentMethodLabel == "Visa •••• 4242");
        storage.AutoRenew!.Status.Should().Be(AutoRenewMandateStatus.Active);
    }
}

/// <summary>Reading PayMongo's recurring-billing documents.</summary>
public class PayMongoRecurringDocumentsTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void AnInvoiceEvent_NamesItsSubscription_AmountAndIntent()
    {
        var root = Parse("""
        {"data":{"id":"evt_1","type":"event","attributes":{"type":"subscription.invoice.paid","livemode":false,
          "data":{"id":"inv_9","type":"invoice","attributes":{"amount":499900,"currency":"PHP","status":"paid",
            "due_date":"2026-11-01","resource_id":"subs_7","payment_intent":{"id":"pi_9","status":"succeeded"}}}}}}
        """);

        var received = PayMongoDocuments.ReadEvent(root)!;

        received.Kind.Should().Be(GatewayEventKind.SubscriptionInvoicePaid);
        received.Invoice!.InvoiceId.Should().Be("inv_9");
        received.Invoice.SubscriptionId.Should().Be("subs_7");
        received.Invoice.Amount.Should().Be(4999m);
        received.Invoice.PaymentIntentId.Should().Be("pi_9");
        received.Invoice.DueDateUtc.Should().Be(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ASubscriptionEvent_GivesItsStatus()
    {
        var root = Parse("""
        {"data":{"id":"evt_2","attributes":{"type":"subscription.unpaid","livemode":false,
          "data":{"id":"subs_7","type":"subscription","attributes":{"status":"unpaid","next_billing_schedule":"2026-12-01"}}}}}
        """);

        var received = PayMongoDocuments.ReadEvent(root)!;

        received.Kind.Should().Be(GatewayEventKind.SubscriptionChanged);
        received.Subscription!.Status.Should().Be("unpaid");
        received.Subscription.NextBillingDateUtc.Should().Be(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ASubscription_GivesItsFirstInvoicesPaymentIntent()
    {
        var subscription = PayMongoDocuments.ReadSubscription(Parse("""
        {"id":"subs_7","type":"subscription","attributes":{"status":"incomplete","customer_id":"cus_1",
          "plan":{"id":"plan_1"},"next_billing_schedule":"2026-11-01",
          "latest_invoice":{"id":"inv_1","status":"open","payment_intent":{"id":"pi_1","status":"awaiting_payment_method"}}}}
        """));

        subscription.PaymentIntentId.Should().Be("pi_1");
        subscription.PlanId.Should().Be("plan_1");
        subscription.LatestInvoiceStatus.Should().Be("open");
    }

    [Fact]
    public void APaidIntent_LabelsTheCard_WithNoMoreThanItsLastFourDigits()
    {
        var intent = PayMongoDocuments.ReadIntent(Parse("""
        {"id":"pi_1","attributes":{"status":"succeeded","payments":[{"id":"pay_1","attributes":{"status":"paid",
          "amount":499900,"currency":"PHP","paid_at":1790000000,"source":{"type":"card","brand":"visa","last4":"4242"}}}]}}
        """));

        intent.Payment!.PaymentId.Should().Be("pay_1");
        intent.Payment.Method.Should().Be(PaymentMethod.Card);
        intent.MethodLabel.Should().Be("Visa •••• 4242");
    }

    [Fact]
    public void AnAttach_GivesTheAuthorizationPage()
    {
        var attach = PayMongoDocuments.ReadAttach(Parse("""
        {"id":"pi_1","attributes":{"status":"awaiting_next_action","next_action":{"type":"redirect",
          "redirect":{"url":"https://3ds.test/x","return_url":"https://hris.test/billing/payment"}}}}
        """));

        attach.RedirectUrl.Should().Be("https://3ds.test/x");
    }
}
