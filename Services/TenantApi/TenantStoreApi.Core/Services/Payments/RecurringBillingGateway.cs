using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Payments;

/// <summary>Who the gateway bills (PayMongo customer).</summary>
public sealed record GatewayCustomerRequest(string FirstName, string LastName, string Email, string? Phone = null);

/// <summary>A gateway subscription as it stands, with its latest invoice and that invoice's payment intent.</summary>
public sealed record GatewayBillingSubscription(
    string SubscriptionId,
    string Status,
    string? CustomerId,
    string? PlanId,
    DateTime? NextBillingDateUtc,
    string? LatestInvoiceId,
    string? LatestInvoiceStatus,
    string? PaymentIntentId,
    string? PaymentIntentStatus);

/// <summary>A payment method attached to an intent: where it stands and, when the payer has to
/// authorize it (3-D Secure, Maya), the page to send them to.</summary>
public sealed record GatewayIntentAttach(string Status, string? RedirectUrl);

/// <summary>A payment intent as it stands; <see cref="Payment"/> once paid.</summary>
public sealed record GatewayIntentState(
    string IntentId,
    string Status,
    GatewayPayment? Payment,
    /// <summary>How it was paid, as the owner should recognize it: "Visa •••• 4242", "Maya".</summary>
    string? MethodLabel,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool IsPaid => Payment != null;
}

/// <summary>One billing cycle's invoice of a gateway subscription.</summary>
public sealed record GatewayInvoice(
    string InvoiceId,
    string? SubscriptionId,
    decimal Amount,
    string Currency,
    string Status,
    DateTime? DueDateUtc,
    string? PaymentIntentId);

/// <summary>
/// Recurring billing at the payment gateway (PayMongo Subscriptions): the only way it charges a saved
/// card or wallet without the payer present. Interface Segregation -- kept apart from
/// <see cref="IPaymentGateway"/>'s hosted checkout, which most of the app needs alone. Adapter: the
/// auto-renew flow (<c>AutoRenewService</c>) talks only to this.
/// </summary>
public interface IRecurringBillingGateway
{
    string Provider { get; }
    bool IsEnabled { get; }
    /// <summary>The publishable key the browser uses to hand card details straight to the gateway.</summary>
    string? PublicKey { get; }

    Task<string> CreateCustomerAsync(GatewayCustomerRequest customer, CancellationToken token);

    /// <summary>A gateway plan billing <paramref name="amount"/> every month or year.</summary>
    Task<string> CreatePlanAsync(string name, string description, decimal amount, string currency, BillingCycle cycle,
        IReadOnlyDictionary<string, string> metadata, CancellationToken token);

    /// <summary>Subscribes the customer; the gateway raises the first invoice and its payment intent at once.</summary>
    Task<GatewayBillingSubscription> CreateSubscriptionAsync(string customerId, string planId, CancellationToken token);
    Task<GatewayBillingSubscription> GetSubscriptionAsync(string subscriptionId, CancellationToken token);
    /// <summary>Stops it at once: no new invoices.</summary>
    Task CancelSubscriptionAsync(string subscriptionId, CancellationToken token);
    /// <summary>Bills on another plan from the next cycle on.</summary>
    Task ChangeSubscriptionPlanAsync(string subscriptionId, string planId, CancellationToken token);
    /// <summary>Adds (or, negative, takes off) an amount on a draft invoice before it is charged.</summary>
    Task AddInvoiceLineItemAsync(string invoiceId, decimal amount, string description, CancellationToken token);

    /// <summary>A wallet payment method created server-side (no card details involved), e.g. Maya.</summary>
    Task<string> CreateWalletPaymentMethodAsync(string type, string name, string email, CancellationToken token);
    Task<GatewayIntentAttach> AttachPaymentMethodAsync(string intentId, string paymentMethodId, string returnUrl, CancellationToken token);
    Task<GatewayIntentState> GetPaymentIntentAsync(string intentId, CancellationToken token);
}

/// <summary>Null Object for <see cref="IRecurringBillingGateway"/> while online payment isn't configured.</summary>
public sealed class DisabledRecurringBillingGateway : IRecurringBillingGateway
{
    private static PaymentGatewayException Off() => new("Online payment isn't configured.");

    public string Provider => "none";
    public bool IsEnabled => false;
    public string? PublicKey => null;

    public Task<string> CreateCustomerAsync(GatewayCustomerRequest customer, CancellationToken token) => throw Off();
    public Task<string> CreatePlanAsync(string name, string description, decimal amount, string currency, BillingCycle cycle,
        IReadOnlyDictionary<string, string> metadata, CancellationToken token) => throw Off();
    public Task<GatewayBillingSubscription> CreateSubscriptionAsync(string customerId, string planId, CancellationToken token) => throw Off();
    public Task<GatewayBillingSubscription> GetSubscriptionAsync(string subscriptionId, CancellationToken token) => throw Off();
    public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken token) => throw Off();
    public Task ChangeSubscriptionPlanAsync(string subscriptionId, string planId, CancellationToken token) => throw Off();
    public Task AddInvoiceLineItemAsync(string invoiceId, decimal amount, string description, CancellationToken token) => throw Off();
    public Task<string> CreateWalletPaymentMethodAsync(string type, string name, string email, CancellationToken token) => throw Off();
    public Task<GatewayIntentAttach> AttachPaymentMethodAsync(string intentId, string paymentMethodId, string returnUrl, CancellationToken token) => throw Off();
    public Task<GatewayIntentState> GetPaymentIntentAsync(string intentId, CancellationToken token) => throw Off();
}
