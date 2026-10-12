using FluentAssertions;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>An operator sending money back through the gateway, and how the payments list then reads.</summary>
public class PaymentRefundTests
{
    private const string Operator = "platform:ops@onepunch.test";

    private static PlatformAccountActionsService Actions(SubscriptionTestHost host) => new(
        host.Uow, host.Subscriptions, host.Events, host.Notifier, host.StateMachine,
        Microsoft.Extensions.Options.Options.Create(host.Options), host.Gateway, host.Clock);

    private static PlatformDirectoryService Directory(SubscriptionTestHost host) => new(host.Uow, host.StateMachine);

    /// <summary>An account that paid Payroll (4,999) online through the gateway.</summary>
    private static async Task<(Guid Owner, SubscriptionPayment Payment)> PaidOnlineAsync(SubscriptionTestHost host)
    {
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");
        await host.SaveAsync();
        await host.Checkouts.StartAsync(owner, owner);
        await host.Checkouts.HandleWebhookAsync(host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m)), FakePaymentGateway.ValidSignature);
        return (owner, host.Uow.Context.SubscriptionPayments.Single());
    }

    private static List<BillingEvent> EventsOf(SubscriptionTestHost host, string type) =>
        host.Uow.Context.BillingEvents.Where(e => e.Type == type).ToList();

    [Fact]
    public async Task Refund_SendsTheAmountToTheGateway_RecordsIt_AndChangesNothingElse()
    {
        var host = new SubscriptionTestHost();
        var (owner, payment) = await PaidOnlineAsync(host);
        var before = host.SubscriptionOf(owner);
        var (status, planId, periodEnd) = (before.SubStatus, before.PlanId, before.CurrentPeriodEndUtc);

        var refund = await Actions(host).RefundPaymentAsync(payment.Id, 1000m, GatewayRefundReason.Duplicate, "  Paid twice ", Operator);
        await host.SaveAsync();

        refund.Amount.Should().Be(1000m);
        host.Gateway.RefundRequests.Should().ContainSingle().Which.Should().Be(("pay_cs_1", 1000m, GatewayRefundReason.Duplicate, (string?)"Paid twice"));
        var recorded = EventsOf(host, BillingEventTypes.PaymentRefundIssued).Should().ContainSingle().Subject;
        recorded.Actor.Should().Be(Operator);
        recorded.OwnerUserId.Should().Be(owner);
        recorded.PayloadJson.Should().Contain("pay_cs_1").And.Contain("1000");

        var after = host.SubscriptionOf(owner);
        (after.SubStatus, after.PlanId, after.CurrentPeriodEndUtc).Should().Be((status, planId, periodEnd));
        host.Uow.Context.SubscriptionPayments.Single().VoidedAtUtc.Should().BeNull("refunding is not voiding");
    }

    [Fact]
    public async Task Refunds_AddUp_AndCannotPassWhatIsLeft()
    {
        var host = new SubscriptionTestHost();
        var (_, payment) = await PaidOnlineAsync(host);
        var actions = Actions(host);

        await actions.RefundPaymentAsync(payment.Id, 4000m, GatewayRefundReason.RequestedByCustomer, null, Operator);
        await host.SaveAsync();
        var tooMuch = () => actions.RefundPaymentAsync(payment.Id, 1000m, GatewayRefundReason.RequestedByCustomer, null, Operator);
        (await tooMuch.Should().ThrowAsync<SubscriptionRuleException>()).Which.Message.Should().Contain("999.00");

        await actions.RefundPaymentAsync(payment.Id, 999m, GatewayRefundReason.RequestedByCustomer, null, Operator);
        await host.SaveAsync();
        var gone = () => actions.RefundPaymentAsync(payment.Id, 1m, GatewayRefundReason.RequestedByCustomer, null, Operator);
        (await gone.Should().ThrowAsync<SubscriptionRuleException>()).Which.Message.Should().Contain("fully refunded");

        host.Gateway.RefundRequests.Select(r => r.Amount).Should().Equal(4000m, 999m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.004)]
    public async Task Refund_OfNothing_IsRejected(double amount)
    {
        var host = new SubscriptionTestHost();
        var (_, payment) = await PaidOnlineAsync(host);

        var act = () => Actions(host).RefundPaymentAsync(payment.Id, (decimal)amount, GatewayRefundReason.Others, null, Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
        host.Gateway.RefundRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Refund_OfAPaymentRecordedByHand_IsRejected_BecauseTheGatewayNeverTookIt()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        var payment = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999, Method = PaymentMethod.BankTransfer, PlanCode = "payroll" }, "platform");
        await host.SaveAsync();

        var act = () => Actions(host).RefundPaymentAsync(payment.Id, 100m, GatewayRefundReason.Others, null, Operator);

        (await act.Should().ThrowAsync<SubscriptionRuleException>()).Which.Message.Should().Contain("payment gateway");
        host.Gateway.RefundRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Refund_TheGatewayRefuses_RecordsNothing()
    {
        var host = new SubscriptionTestHost();
        var (_, payment) = await PaidOnlineAsync(host);
        host.Gateway.FailNextRefund = true;

        var act = () => Actions(host).RefundPaymentAsync(payment.Id, 100m, GatewayRefundReason.Others, null, Operator);
        await act.Should().ThrowAsync<PaymentGatewayException>();
        await host.SaveAsync();

        EventsOf(host, BillingEventTypes.PaymentRefundIssued).Should().BeEmpty();
    }

    [Fact]
    public async Task Refund_WithOnlinePaymentSwitchedOff_IsRejected()
    {
        var host = new SubscriptionTestHost();
        var (_, payment) = await PaidOnlineAsync(host);
        host.Gateway.IsEnabled = false;

        var act = () => Actions(host).RefundPaymentAsync(payment.Id, 100m, GatewayRefundReason.Others, null, Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    // ── The payments list ────────────────────────────────────────────────────

    [Fact]
    public async Task TheList_ShowsHowMuchWasRefunded_AndWhetherMoreCanBe()
    {
        var host = new SubscriptionTestHost();
        var (_, payment) = await PaidOnlineAsync(host);

        var untouched = (await Directory(host).ListPaymentsAsync(new PaymentQuery())).Items.Single();
        untouched.CanRefund.Should().BeTrue();
        untouched.RefundedAmount.Should().Be(0m);
        untouched.GatewayIssue.Should().BeNull();

        await Actions(host).RefundPaymentAsync(payment.Id, 1000m, GatewayRefundReason.Others, null, Operator);
        await host.SaveAsync();
        var partial = (await Directory(host).ListPaymentsAsync(new PaymentQuery())).Items.Single();
        partial.RefundedAmount.Should().Be(1000m);
        partial.CanRefund.Should().BeTrue();
        partial.GatewayIssue.Should().Be("Refunded");

        await Actions(host).RefundPaymentAsync(payment.Id, 3999m, GatewayRefundReason.Others, null, Operator);
        await host.SaveAsync();
        var whole = (await Directory(host).ListPaymentsAsync(new PaymentQuery())).Items.Single();
        whole.RefundedAmount.Should().Be(4999m);
        whole.CanRefund.Should().BeFalse();
    }

    [Fact]
    public async Task APaymentRecordedByHand_CannotBeRefundedFromTheList()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999, Method = PaymentMethod.Cash, PlanCode = "payroll" }, "platform");
        await host.SaveAsync();

        (await Directory(host).ListPaymentsAsync(new PaymentQuery())).Items.Single().CanRefund.Should().BeFalse();
    }

    [Fact]
    public async Task NeedsReview_IsARefundNobodyIssuedHere_ButNotOneAnOperatorSent()
    {
        var host = new SubscriptionTestHost();
        var (_, payment) = await PaidOnlineAsync(host);
        var directory = Directory(host);
        (await directory.ListPaymentsAsync(new PaymentQuery(NeedsReview: true))).Items.Should().BeEmpty();

        // Refunded in the gateway's own dashboard: the webhook reports it and nobody here dealt with it.
        await host.Checkouts.HandleWebhookAsync(
            host.Gateway.DeliverIssue("pay_cs_1", GatewayEventKind.PaymentRefunded, 500m), FakePaymentGateway.ValidSignature);
        var flagged = (await directory.ListPaymentsAsync(new PaymentQuery(NeedsReview: true))).Items.Should().ContainSingle().Subject;
        flagged.NeedsReview.Should().BeTrue();
        flagged.RefundedAmount.Should().Be(500m);

        // An operator refunds it from the console: that is handling it.
        await Actions(host).RefundPaymentAsync(payment.Id, 500m, GatewayRefundReason.Others, null, Operator);
        await host.SaveAsync();
        (await directory.ListPaymentsAsync(new PaymentQuery(NeedsReview: true))).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkingGatewayIssueReviewed_ReleasesItsCountWithoutChangingPayment_AndNewIssuesAreCounted()
    {
        var host = new SubscriptionTestHost();
        var (_, payment) = await PaidOnlineAsync(host);
        var directory = Directory(host);
        await host.Checkouts.HandleWebhookAsync(
            host.Gateway.DeliverIssue("pay_cs_1", GatewayEventKind.PaymentRefunded, 500m), FakePaymentGateway.ValidSignature);
        var actions = Actions(host);

        await actions.MarkPaymentIssueReviewedAsync(payment.Id, Operator);
        await host.SaveAsync();

        var listedPayment = (await directory.ListPaymentsAsync(new PaymentQuery())).Items.Single();
        listedPayment.NeedsReview.Should().BeFalse();
        listedPayment.GatewayIssue.Should().Be("Refunded");
        listedPayment.RefundedAmount.Should().Be(500m);
        host.Uow.Context.SubscriptionPayments.Single().VoidedAtUtc.Should().BeNull();
        EventsOf(host, BillingEventTypes.PaymentIssueReviewed).Should().ContainSingle().Which.Actor.Should().Be(Operator);
        (await directory.ListPaymentsAsync(new PaymentQuery(NeedsReview: true))).Items.Should().BeEmpty();

        await host.Checkouts.HandleWebhookAsync(
            host.Gateway.DeliverIssue("pay_cs_1", GatewayEventKind.PaymentDisputed, 4999m), FakePaymentGateway.ValidSignature);
        (await directory.ListPaymentsAsync(new PaymentQuery(NeedsReview: true))).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task NeedsReview_IncludesADispute_AndDropsAPaymentOnceItIsVoided()
    {
        var host = new SubscriptionTestHost();
        var (_, payment) = await PaidOnlineAsync(host);
        await host.Checkouts.HandleWebhookAsync(
            host.Gateway.DeliverIssue("pay_cs_1", GatewayEventKind.PaymentDisputed, 4999m), FakePaymentGateway.ValidSignature);
        (await Directory(host).ListPaymentsAsync(new PaymentQuery(NeedsReview: true))).Items.Should().ContainSingle();

        await Actions(host).VoidPaymentAsync(payment.Id, "Charged back", Operator, VoidKind.Cancelled);
        await host.SaveAsync();

        (await Directory(host).ListPaymentsAsync(new PaymentQuery(NeedsReview: true))).Items.Should().BeEmpty();
    }

    // ── Voiding with a plan rollback ─────────────────────────────────────────

    private static async Task<(Guid Owner, SubscriptionPayment First, SubscriptionPayment Upgrade)> UpgradedAsync(SubscriptionTestHost host)
    {
        var (owner, _) = await host.TrialAccountAsync();
        var first = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 1500, Method = PaymentMethod.BankTransfer, PlanCode = "timekeeping" }, "platform");
        await host.SaveAsync();
        host.Clock.Advance(TimeSpan.FromDays(10));
        var upgrade = await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand { Amount = 4999, Method = PaymentMethod.BankTransfer, PlanCode = "payroll" }, "platform");
        await host.SaveAsync();
        return (owner, first, upgrade);
    }

    [Fact]
    public async Task Void_WithRevertPlan_PutsTheAccountBackOnThePreviousPaymentsPlan()
    {
        var host = new SubscriptionTestHost();
        var (owner, first, upgrade) = await UpgradedAsync(host);
        host.SubscriptionOf(owner).Plan.Code.Should().Be("payroll");

        await Actions(host).VoidPaymentAsync(upgrade.Id, "Refunded", Operator, VoidKind.Refunded, revertPlan: true);
        await host.SaveAsync();

        var subscription = host.SubscriptionOf(owner);
        subscription.PlanId.Should().Be(first.PlanId);
        subscription.CurrentPeriodEndUtc.Should().Be(first.PeriodEndUtc);
        EventsOf(host, BillingEventTypes.PaymentVoided).Should().ContainSingle().Which.PayloadJson.Should().Contain("\"planReverted\":{\"from\":\"payroll\",\"to\":\"timekeeping\"}");
    }

    [Fact]
    public async Task Void_WithoutRevertPlan_LeavesThePlanAlone()
    {
        var host = new SubscriptionTestHost();
        var (owner, _, upgrade) = await UpgradedAsync(host);

        await Actions(host).VoidPaymentAsync(upgrade.Id, "Refunded", Operator, VoidKind.Refunded);
        await host.SaveAsync();

        host.SubscriptionOf(owner).Plan.Code.Should().Be("payroll");
    }

    [Fact]
    public async Task Void_WithRevertPlan_WhenThereIsNoPreviousPayment_ChangesNoPlan()
    {
        var host = new SubscriptionTestHost();
        var (owner, payment) = await PaidOnlineAsync(host);

        await Actions(host).VoidPaymentAsync(payment.Id, "Refunded", Operator, VoidKind.Refunded, revertPlan: true);
        await host.SaveAsync();

        host.SubscriptionOf(owner).Plan.Code.Should().Be("payroll");
    }
}
