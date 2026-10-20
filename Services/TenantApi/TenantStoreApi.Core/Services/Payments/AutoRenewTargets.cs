using System.Text.Json;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Payments;

/// <summary>Which charge turns auto renew on: the plan or storage due now, or (storage) a purchase that starts its period.</summary>
public sealed record StartAutoRenewCommand
{
    public AutoRenewTarget Target { get; init; }
    /// <summary>Storage only: buy blocks that start a new storage period, rather than pay a storage renewal.</summary>
    public string? OfferingCode { get; init; }
    public int? Blocks { get; init; }
    /// <summary>Storage purchase only: the cycle a new storage period runs on.</summary>
    public BillingCycle? BillingCycle { get; init; }
}

/// <summary>One charge an auto renew makes: how much, every month or year, and what it's called.</summary>
public sealed record AutoRenewCharge(decimal Amount, string Currency, BillingCycle Cycle, string Name);

/// <summary>The first charge, and the checkout that settles it through the ordinary payment paths.</summary>
public sealed record AutoRenewFirstCharge(AutoRenewCharge Charge, PaymentCheckout Checkout);

/// <summary>
/// Strategy: what auto renew does for one target -- the plan or storage, billed independently. The
/// flow (<see cref="AutoRenewService"/>) is the same for both; only these three questions differ.
/// </summary>
public interface IAutoRenewTarget
{
    AutoRenewTarget Target { get; }

    /// <summary>What paying now buys, with the checkout (unsaved) to settle it. Throws a rule exception when nothing can be paid now.</summary>
    Task<AutoRenewFirstCharge> FirstChargeAsync(AccountSubscription subscription, StartAutoRenewCommand command, CancellationToken token);

    /// <summary>What the next cycle should cost; null when it shouldn't renew at all.</summary>
    Task<AutoRenewCharge?> NextChargeAsync(AccountSubscription subscription, CancellationToken token);

    /// <summary>Records a renewal the gateway charged, as a renewal paid by hand would be.</summary>
    Task SettleRenewalAsync(AccountSubscription subscription, decimal amount, GatewayPayment payment, string invoiceId, string actor, CancellationToken token);
}

/// <summary>The plan's next period, at this account's price (a scheduled downgrade's plan when one is due).</summary>
public sealed class PlanAutoRenewTarget(AccountSubscriptionService subscriptions) : IAutoRenewTarget
{
    public AutoRenewTarget Target => AutoRenewTarget.Plan;

    public async Task<AutoRenewFirstCharge> FirstChargeAsync(AccountSubscription subscription, StartAutoRenewCommand command, CancellationToken token)
    {
        var due = await subscriptions.GetPaymentDueAsync(subscription, token)
            ?? throw new SubscriptionRuleException("There's nothing to pay for your plan right now. Turn on auto renew when your next payment is due.");
        var checkout = new PaymentCheckout
        {
            Kind = PaymentCheckoutKind.Plan,
            PlanId = due.Plan.Id,
            BillingCycle = due.BillingCycle,
            Amount = due.Amount,
            Currency = due.Plan.Currency,
            LineItemsJson = JsonSerializer.Serialize(SubscriptionCheckoutService.BuildLineItems(due)),
        };
        return new AutoRenewFirstCharge(new AutoRenewCharge(due.Amount, due.Plan.Currency, due.BillingCycle, $"{due.Plan.Name} plan"), checkout);
    }

    public async Task<AutoRenewCharge?> NextChargeAsync(AccountSubscription subscription, CancellationToken token) =>
        await subscriptions.GetNextRenewalAsync(subscription, token) is { } next
            ? new AutoRenewCharge(next.Amount, next.Plan.Currency, next.BillingCycle, $"{next.Plan.Name} plan")
            : null;

    public async Task SettleRenewalAsync(
        AccountSubscription subscription, decimal amount, GatewayPayment payment, string invoiceId, string actor, CancellationToken token)
    {
        var next = await subscriptions.GetNextRenewalAsync(subscription, token);
        await subscriptions.RecordPaymentAsync(subscription.OwnerUserId, new RecordPaymentCommand
        {
            Amount = amount,
            Method = payment.Method,
            Reference = payment.PaymentId,
            PaidAtUtc = payment.PaidAtUtc,
            PlanCode = next?.Plan.Code ?? subscription.Plan.Code,
            BillingCycle = next?.BillingCycle ?? subscription.BillingCycle,
            Notes = $"Auto renew (invoice {invoiceId})",
        }, actor, token);
    }
}

/// <summary>Storage's next period: every paid block, on storage's own cycle.</summary>
public sealed class StorageAutoRenewTarget(StorageSubscriptionService period, StorageAddOnService storage) : IAutoRenewTarget
{
    public AutoRenewTarget Target => AutoRenewTarget.Storage;

    public async Task<AutoRenewFirstCharge> FirstChargeAsync(AccountSubscription subscription, StartAutoRenewCommand command, CancellationToken token)
    {
        if (command.OfferingCode is { } code)
        {
            var quote = await storage.QuoteAsync(subscription.OwnerUserId, code, command.Blocks ?? 0, command.BillingCycle, token);
            if (!quote.StartsNewPeriod)
                throw new SubscriptionRuleException("Auto renew starts with your next storage renewal: storage you add now is prorated to it.");
            var bought = new PaymentCheckout
            {
                Kind = PaymentCheckoutKind.StorageAddOn,
                PlanId = subscription.PlanId,
                BillingCycle = quote.BillingCycle,
                StorageBlocks = quote.Blocks,
                Amount = quote.AmountDue,
                Currency = quote.Currency,
            };
            // Only the offering's id is stored on the checkout: set by the caller, which has the context.
            return new AutoRenewFirstCharge(
                new AutoRenewCharge(quote.AmountDue, quote.Currency, quote.BillingCycle, "Extra storage"), bought);
        }

        var due = await period.GetRenewalDueAsync(subscription, token)
            ?? throw new SubscriptionRuleException("There's no storage renewal to pay right now. Turn on auto renew when your next storage payment is due.");
        var renewal = new PaymentCheckout
        {
            Kind = PaymentCheckoutKind.StorageRenewal,
            PlanId = subscription.PlanId,
            BillingCycle = due.BillingCycle,
            StorageBlocks = due.Blocks,
            Amount = due.Amount,
            Currency = due.Currency,
            LineItemsJson = JsonSerializer.Serialize(due.LineItems),
        };
        return new AutoRenewFirstCharge(new AutoRenewCharge(due.Amount, due.Currency, due.BillingCycle, "Extra storage"), renewal);
    }

    public async Task<AutoRenewCharge?> NextChargeAsync(AccountSubscription subscription, CancellationToken token)
    {
        // Lapsed storage isn't renewed by itself: the owner pays to bring it back.
        if (subscription.StorageSubscription?.Status == StorageSubscriptionStatus.Lapsed) return null;
        return await period.GetRenewalDueAsync(subscription, token, anyTime: true) is { } due
            ? new AutoRenewCharge(due.Amount, due.Currency, due.BillingCycle, "Extra storage")
            : null;
    }

    public Task SettleRenewalAsync(
        AccountSubscription subscription, decimal amount, GatewayPayment payment, string invoiceId, string actor, CancellationToken token) =>
        period.RecordRenewalAsync(subscription.OwnerUserId, amount, payment.Method, payment.PaymentId, actor,
            payment.PaidAtUtc, token, anyTime: true);
}
