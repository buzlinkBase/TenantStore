using System.Text.Json;
using FluentAssertions;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>Upgrading before the paid period ends: the unused days already paid for come off the new plan's price.</summary>
public class UpgradeProrationTests
{
    private static readonly DateTime Start = SubscriptionTestHost.Start;

    // ── The calculator ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Credit_IsTheValueOfTheWholeDaysStillToRun()
    {
        var span = new PaidSpan(3100m, Start, Start.AddDays(31));

        UpgradeProration.CreditFor([span], Start).Should().Be(new UpgradeCredit(3100m, 31));
        UpgradeProration.CreditFor([span], Start.AddDays(15)).Should().Be(new UpgradeCredit(1600m, 16));
        UpgradeProration.CreditFor([span], Start.AddDays(15).AddHours(6)).Should().Be(new UpgradeCredit(1500m, 15), "today counts as used");
        UpgradeProration.CreditFor([span], Start.AddDays(30).AddHours(1)).Should().Be(UpgradeCredit.None, "the last day is in use");
        UpgradeProration.CreditFor([span], Start.AddDays(40)).Should().Be(UpgradeCredit.None, "never negative");
    }

    [Fact]
    public void Credit_RoundsToCentavos_AndSkipsWhatsWorthNothing()
    {
        var spans = new[]
        {
            new PaidSpan(4999m, Start, Start.AddDays(31)),
            new PaidSpan(-10m, Start, Start.AddDays(31)),
            new PaidSpan(100m, Start, Start),
        };

        UpgradeProration.CreditFor(spans, Start.AddDays(15)).Amount.Should().Be(2580.13m, "4,999 x 16 / 31");
    }

    [Fact]
    public void Credit_ForAPeriodPaidAhead_CountsInFull()
    {
        var current = new PaidSpan(3100m, Start, Start.AddDays(31));
        var ahead = new PaidSpan(3000m, Start.AddDays(31), Start.AddDays(61));

        UpgradeProration.CreditFor([current, ahead], Start.AddDays(15)).Should().Be(new UpgradeCredit(4600m, 46));
    }

    [Fact]
    public void Apply_TakesTheCreditOffTheCharge()
    {
        var charge = UpgradeProration.Apply(9999m, new UpgradeCredit(2580.13m, 16), Start, BillingCycle.Monthly);

        charge.AmountDue.Should().Be(7418.87m);
        charge.PeriodEndUtc.Should().Be(Start.AddMonths(1));
        charge.ExtraDays.Should().Be(0);
    }

    [Fact]
    public void Apply_CreditBeyondTheCharge_BuysExtraDays()
    {
        // 3,100 a month of 31 days is 100 a day: 1,050 left over buys 10 whole days.
        var charge = UpgradeProration.Apply(3100m, new UpgradeCredit(4150m, 300), Start, BillingCycle.Monthly);

        charge.AmountDue.Should().Be(0m);
        charge.ExtraDays.Should().Be(10);
        charge.PeriodEndUtc.Should().Be(Start.AddMonths(1).AddDays(10));
    }

    // ── Upgrading an account ─────────────────────────────────────────────────────────────────

    /// <summary>A trial account that paid for Payroll on <paramref name="cycle"/> on 1 October.</summary>
    private static async Task<(SubscriptionTestHost Host, Guid Owner, Guid TenantId)> PaidPayrollAsync(BillingCycle cycle = BillingCycle.Monthly)
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        await host.Subscriptions.RequestPlanChangeAsync(owner, "payroll", cycle, "owner");
        await host.Subscriptions.RecordPaymentAsync(owner, new RecordPaymentCommand
        {
            Amount = cycle == BillingCycle.Annual ? 49990m : 4999m,
            Method = PaymentMethod.BankTransfer,
        }, "platform");
        await host.SaveAsync();
        return (host, owner, tenantId);
    }

    [Fact]
    public async Task UpgradeMidPeriod_IsQuotedAndDue_LessTheUnusedDays()
    {
        var (host, owner, tenantId) = await PaidPayrollAsync();
        host.Clock.UtcNow = Start.AddDays(15);

        var quote = await host.Subscriptions.RequestPlanChangeAsync(owner, "premium", BillingCycle.Monthly, "owner");
        await host.SaveAsync();

        quote.Timing.Should().Be(PlanChangeTiming.OnPayment);
        quote.Subtotal.Should().Be(9999m);
        quote.CreditAmount.Should().Be(2580.13m, "16 of October's 31 days are unused");
        quote.UnusedDays.Should().Be(16);
        quote.AmountDue.Should().Be(7418.87m);
        quote.AppliedImmediately.Should().BeFalse();

        var due = await host.Subscriptions.GetPaymentDueAsync(host.SubscriptionOf(owner));
        due!.Amount.Should().Be(7418.87m);
        due.CreditAmount.Should().Be(2580.13m);
        due.PlanAmount.Should().Be(9999m);

        var view = await host.Views.GetBillingForTenantAsync(tenantId);
        view.PendingChange!.AmountDue.Should().Be(7418.87m);
        view.PaymentDue!.CreditAmount.Should().Be(2580.13m);
    }

    [Fact]
    public async Task PayingTheNetAmount_StartsTheNewPlanToday_AndRecordsTheCredit()
    {
        var (host, owner, _) = await PaidPayrollAsync();
        host.Clock.UtcNow = Start.AddDays(15);
        await host.Subscriptions.RequestPlanChangeAsync(owner, "premium", BillingCycle.Monthly, "owner");
        await host.SaveAsync();

        var payment = await host.Subscriptions.RecordPaymentAsync(owner,
            new RecordPaymentCommand { Amount = 7418.87m, Method = PaymentMethod.GCash }, "platform");
        await host.SaveAsync();

        payment.CreditAmount.Should().Be(2580.13m);
        payment.PeriodStartUtc.Should().Be(Start.AddDays(15));
        payment.PeriodEndUtc.Should().Be(Start.AddDays(15).AddMonths(1));
        JsonSerializer.Deserialize<List<PaymentLineItemSnapshot>>(payment.LineItemsJson!).Should().Equal(
            new PaymentLineItemSnapshot("Premium plan (monthly)", 9999m),
            new PaymentLineItemSnapshot(AccountSubscriptionService.UpgradeCreditLineName, -2580.13m, "16 unused days of the Payroll plan"));
        var subscription = host.SubscriptionOf(owner);
        subscription.Plan.Code.Should().Be("premium");
        subscription.CurrentPeriodStartUtc.Should().Be(Start.AddDays(15));
    }

    [Fact]
    public async Task PayingTheFullPrice_TakesNoCredit()
    {
        var (host, owner, _) = await PaidPayrollAsync();
        host.Clock.UtcNow = Start.AddDays(15);
        await host.Subscriptions.RequestPlanChangeAsync(owner, "premium", BillingCycle.Monthly, "owner");
        await host.SaveAsync();

        var payment = await host.Subscriptions.RecordPaymentAsync(owner,
            new RecordPaymentCommand { Amount = 9999m, Method = PaymentMethod.BankTransfer }, "platform");

        payment.CreditAmount.Should().Be(0m);
        payment.PeriodEndUtc.Should().Be(Start.AddDays(15).AddMonths(1));
    }

    [Fact]
    public async Task ASecondUpgrade_CreditsOnlyTheCurrentPeriod_NeverTheOneAlreadyCredited()
    {
        var (host, owner, _) = await PaidPayrollAsync();
        host.Clock.UtcNow = Start.AddDays(15);
        await host.Subscriptions.RequestPlanChangeAsync(owner, "premium", BillingCycle.Monthly, "owner");
        await host.Subscriptions.RecordPaymentAsync(owner,
            new RecordPaymentCommand { Amount = 7418.87m, Method = PaymentMethod.GCash }, "platform");
        await host.SaveAsync();
        host.Clock.UtcNow = Start.AddDays(25);

        var quote = await host.Subscriptions.RequestPlanChangeAsync(owner, "premium", BillingCycle.Annual, "owner");

        // The Premium month (worth 9,999: 7,418.87 paid + 2,580.13 credit) has 21 of 31 days left.
        quote.CreditAmount.Should().Be(6773.52m);
        quote.AmountDue.Should().Be(99990m - 6773.52m);
    }

    [Fact]
    public async Task CreditCoveringThePrice_SwitchesRightAway_WithTheRestAsExtraDays()
    {
        var (host, owner, _) = await PaidPayrollAsync(BillingCycle.Annual);
        var now = Start.AddDays(1);
        host.Clock.UtcNow = now;

        var quote = await host.Subscriptions.RequestPlanChangeAsync(owner, "premium", BillingCycle.Monthly, "owner");
        await host.SaveAsync();

        // 364 of 365 days of 49,990 is 49,853.04; a Premium month is 9,999 for 31 days (322.55 a day),
        // so the 39,854.04 left over buys 123 more days.
        quote.AppliedImmediately.Should().BeTrue();
        quote.AmountDue.Should().Be(0m);
        quote.CreditAmount.Should().Be(9999m);
        quote.PeriodEndUtc.Should().Be(now.AddMonths(1).AddDays(123));

        var subscription = host.SubscriptionOf(owner);
        subscription.Plan.Code.Should().Be("premium");
        subscription.BillingCycle.Should().Be(BillingCycle.Monthly);
        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Active);
        subscription.PendingPlanId.Should().BeNull();
        subscription.CurrentPeriodEndUtc.Should().Be(now.AddMonths(1).AddDays(123));

        var payment = host.Uow.Context.SubscriptionPayments.OrderByDescending(p => p.PaidAtUtc).First();
        payment.Amount.Should().Be(0m);
        payment.Method.Should().Be(PaymentMethod.Credit);
        payment.CreditAmount.Should().Be(49853.04m);
        (await host.Subscriptions.GetPaymentDueAsync(subscription)).Should().BeNull("nothing is left to pay");
    }

    [Fact]
    public async Task APeriodNobodyPaidFor_EarnsNoCredit()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner,
            new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = Start.AddMonths(1) }, "platform");
        await host.SaveAsync();
        host.Clock.UtcNow = Start.AddDays(15);

        var quote = await host.Subscriptions.RequestPlanChangeAsync(owner, "premium", BillingCycle.Monthly, "owner");

        quote.CreditAmount.Should().Be(0m);
        quote.AmountDue.Should().Be(9999m);
    }

    [Fact]
    public async Task PastDue_EarnsNoCredit()
    {
        var (host, owner, _) = await PaidPayrollAsync();
        host.Clock.UtcNow = Start.AddMonths(1).AddDays(2);
        host.SubscriptionOf(owner).SubStatus = AccountSubscriptionStatus.PastDue;
        await host.SaveAsync();

        (await host.Subscriptions.GetUpgradeCreditAsync(host.SubscriptionOf(owner))).Should().Be(UpgradeCredit.None);
    }

    // ── Paying online ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GatewayLines_AreNeverNegative_AndAddUpToTheAmount()
    {
        var plan = new Plan { Name = "Premium", Currency = "PHP" };
        var upgrade = new PaymentDue(plan, BillingCycle.Monthly, 7418.87m, PaymentDueReason.PlanChange, 2580.13m, 16);
        var renewal = new PaymentDue(plan, BillingCycle.Monthly, 9999m, PaymentDueReason.Renewal);

        foreach (var due in new[] { upgrade, renewal })
        {
            var lines = SubscriptionCheckoutService.GatewayLineItems(due);
            lines.Should().OnlyContain(line => line.Amount > 0);
            lines.Sum(line => line.Amount).Should().Be(due.Amount);
        }
        SubscriptionCheckoutService.GatewayLineItems(upgrade).Select(l => l.Amount).Should().Equal(7418.87m);
        SubscriptionCheckoutService.GatewayLineItems(upgrade).Single().Description.Should().StartWith("Less ");
        SubscriptionCheckoutService.GatewayLineItems(renewal).Should().ContainSingle("storage is never on a plan checkout");
    }

    [Fact]
    public async Task Checkout_ChargesTheNetAmount_AndSettlesWithTheQuotedCredit()
    {
        var (host, owner, _) = await PaidPayrollAsync();
        host.Clock.UtcNow = Start.AddDays(15);
        await host.Subscriptions.RequestPlanChangeAsync(owner, "premium", BillingCycle.Monthly, "owner");
        await host.SaveAsync();

        var started = await host.Checkouts.StartAsync(owner, owner);

        var request = host.Gateway.Created.Single();
        request.Amount.Should().Be(7418.87m);
        request.LineItems!.Should().ContainSingle().Which.Amount.Should().Be(7418.87m);
        var checkout = host.Uow.Context.PaymentCheckouts.Single(c => c.Id == started.CheckoutId);
        JsonSerializer.Deserialize<List<PaymentLineItemSnapshot>>(checkout.LineItemsJson!)
            .Select(l => l.Amount).Should().Equal(9999m, -2580.13m);

        // Paid the next morning: the credit stays what the page quoted.
        host.Clock.UtcNow = Start.AddDays(16).AddHours(8);
        var outcome = await host.Checkouts.HandleWebhookAsync(
            host.Gateway.Deliver(host.Gateway.Pay("cs_1", 7418.87m)), FakePaymentGateway.ValidSignature);

        outcome.Should().Be(WebhookOutcome.Settled);
        var payment = host.Uow.Context.SubscriptionPayments.Local.Single(p => p.Amount == 7418.87m);
        payment.CreditAmount.Should().Be(2580.13m);
        host.SubscriptionOf(owner).Plan.Code.Should().Be("premium");
    }
}
