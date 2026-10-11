using FluentAssertions;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>The client's billing page shows a refund as soon as it happens, voided or not.</summary>
public class ClientPaymentRefundViewTests
{
    private const string Operator = "platform:ops@onepunch.test";

    private static PlatformAccountActionsService Actions(SubscriptionTestHost host) => new(
        host.Uow, host.Subscriptions, host.Events, host.Notifier, host.StateMachine,
        Microsoft.Extensions.Options.Options.Create(host.Options), host.Gateway, host.Clock);

    private static async Task<(Guid Owner, SubscriptionPayment Payment)> PaidOnlineAsync(SubscriptionTestHost host, string email = "owner@acme.ph")
    {
        var (owner, _) = await host.TrialAccountAsync(email);
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", BillingCycle.Monthly, "owner");
        await host.SaveAsync();
        await host.Checkouts.StartAsync(owner, owner);
        await host.Checkouts.HandleWebhookAsync(host.Gateway.Deliver(host.Gateway.Pay("cs_1", 4999m)), FakePaymentGateway.ValidSignature);
        return (owner, host.Uow.Context.SubscriptionPayments.Single());
    }

    private static async Task<PaymentView> ViewOfAsync(SubscriptionTestHost host, Guid owner) =>
        (await host.Views.GetBillingForAccountAsync(owner)).Payments.Single(p => p.Kind == PaymentKinds.Plan);

    [Fact]
    public async Task APaymentNobodyRefunded_StaysPaid()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await PaidOnlineAsync(host);

        var view = await ViewOfAsync(host, owner);

        view.Status.Should().Be(PaymentStatuses.Paid);
        view.RefundedAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ARefundFromTheConsole_ShowsAtOnce_WithoutVoiding()
    {
        var host = new SubscriptionTestHost();
        var (owner, payment) = await PaidOnlineAsync(host);

        await Actions(host).RefundPaymentAsync(payment.Id, 1000m, GatewayRefundReason.Others, "private note", Operator);
        await host.SaveAsync();
        var partial = await ViewOfAsync(host, owner);
        await Actions(host).RefundPaymentAsync(payment.Id, 3999m, GatewayRefundReason.Others, null, Operator);
        await host.SaveAsync();
        var whole = await ViewOfAsync(host, owner);

        partial.Status.Should().Be(PaymentStatuses.PartiallyRefunded);
        partial.RefundedAmount.Should().Be(1000m);
        whole.Status.Should().Be(PaymentStatuses.Refunded);
        whole.RefundedAmount.Should().Be(4999m);
        host.Uow.Context.SubscriptionPayments.Single().VoidedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ARefundMadeInTheGatewaysDashboard_ShowsToo()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await PaidOnlineAsync(host);

        await host.Checkouts.HandleWebhookAsync(
            host.Gateway.DeliverIssue("pay_cs_1", GatewayEventKind.PaymentRefunded, 500m), FakePaymentGateway.ValidSignature);

        var view = await ViewOfAsync(host, owner);
        view.Status.Should().Be(PaymentStatuses.PartiallyRefunded);
        view.RefundedAmount.Should().Be(500m);
    }

    [Fact]
    public async Task ADispute_IsNotShownToTheClient()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await PaidOnlineAsync(host);

        await host.Checkouts.HandleWebhookAsync(
            host.Gateway.DeliverIssue("pay_cs_1", GatewayEventKind.PaymentDisputed, 4999m), FakePaymentGateway.ValidSignature);

        (await ViewOfAsync(host, owner)).Status.Should().Be(PaymentStatuses.Paid);
    }

    [Fact]
    public async Task VoidingAsRefunded_StillShowsRefunded_AndTheRefundedAmountStaysVisible()
    {
        var host = new SubscriptionTestHost();
        var (owner, payment) = await PaidOnlineAsync(host);
        await Actions(host).RefundPaymentAsync(payment.Id, 1000m, GatewayRefundReason.Others, null, Operator);
        await host.SaveAsync();

        await Actions(host).VoidPaymentAsync(payment.Id, "Refunded", Operator, VoidKind.Refunded);
        await host.SaveAsync();

        var view = await ViewOfAsync(host, owner);
        view.Status.Should().Be("Refunded", "the void kind decides once voided");
        view.RefundedAmount.Should().Be(1000m);
    }

    [Fact]
    public async Task OnlyThatAccountsRefunds_AreShown_AndNoReasonOrNoteIsExposed()
    {
        var host = new SubscriptionTestHost();
        var (owner, payment) = await PaidOnlineAsync(host);
        await Actions(host).RefundPaymentAsync(payment.Id, 1000m, GatewayRefundReason.Fraudulent, "private note", Operator);
        await host.SaveAsync();
        var (other, _) = await host.TrialAccountAsync("other@acme.ph");

        var billing = await host.Views.GetBillingForAccountAsync(owner);

        (await host.Views.GetBillingForAccountAsync(other)).Payments.Should().BeEmpty();
        var json = System.Text.Json.JsonSerializer.Serialize(billing);
        json.Should().NotContain("private note").And.NotContain("Fraudulent").And.NotContain(Operator);
    }
}
