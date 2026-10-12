using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Payments;

/// <summary>A hosted payment page to open for one quoted amount.</summary>
public sealed record GatewayCheckoutRequest
{
    public required Guid CheckoutId { get; init; }
    /// <summary>The line the payer sees, e.g. "Payroll plan (monthly)".</summary>
    public required string ItemName { get; init; }
    /// <summary>Optional itemized lines. When absent, the gateway uses <see cref="ItemName"/> and <see cref="Amount"/>.</summary>
    public IReadOnlyList<GatewayCheckoutLineItem>? LineItems { get; init; }
    public required string Description { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string SuccessUrl { get; init; }
    public required string CancelUrl { get; init; }
    /// <summary>Echoed back on the session and its webhook -- how a payment finds its checkout.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

/// <summary>One separately priced item shown on a hosted checkout page.</summary>
public sealed record GatewayCheckoutLineItem(string Name, decimal Amount, string? Description = null);

public sealed record GatewayCheckoutSession(string SessionId, string CheckoutUrl);

/// <summary>A completed payment, as the gateway reports it.</summary>
public sealed record GatewayPayment(
    string PaymentId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    string? MethodName,
    DateTime? PaidAtUtc);

/// <summary>A hosted checkout session as the gateway reports it -- paid once it holds a paid payment.</summary>
public sealed record GatewaySessionState(
    string SessionId,
    GatewayPayment? Payment,
    IReadOnlyDictionary<string, string> Metadata,
    bool LiveMode)
{
    public bool IsPaid => Payment != null;
}

/// <summary>A payment as the gateway reports it now: how much of it has been given back.</summary>
public sealed record GatewayPaymentState(string PaymentId, decimal Amount, decimal RefundedAmount);

/// <summary>Why a refund is issued -- the reasons a gateway accepts (PayMongo's own list).</summary>
public enum GatewayRefundReason
{
    RequestedByCustomer,
    Duplicate,
    Fraudulent,
    Others,
}

/// <summary>A refund the gateway accepted: its id, the payment it came off, and how much.</summary>
public sealed record GatewayRefund(string RefundId, string PaymentId, decimal Amount, string Status);

public enum GatewayEventKind
{
    CheckoutPaid,
    /// <summary>Money was given back to the payer (done in the gateway's dashboard).</summary>
    PaymentRefunded,
    /// <summary>The payer disputed the payment with their bank or wallet.</summary>
    PaymentDisputed,
    Other,
}

/// <summary>A refund or dispute against a payment we took: which gateway payment, and for how much.</summary>
public sealed record GatewayPaymentIssue(string PaymentId, decimal? Amount);

/// <summary>An authentic webhook delivery. <see cref="Session"/> is set for checkout events,
/// <see cref="Issue"/> for refunds and disputes.</summary>
public sealed record GatewayWebhookEvent(
    string EventId,
    string Type,
    GatewayEventKind Kind,
    bool LiveMode,
    GatewaySessionState? Session,
    GatewayPaymentIssue? Issue = null);

/// <summary>
/// A payment provider's hosted checkout. Strategy/Adapter: the checkout flow
/// (<see cref="SubscriptionCheckoutService"/>) only talks to this, so another provider is one more
/// implementation; <see cref="PayMongoGateway"/> adapts PayMongo's API to it.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Stable name stored on each checkout, e.g. "paymongo".</summary>
    string Provider { get; }
    bool IsEnabled { get; }
    /// <summary>Real money (live keys) or test mode. Events from the other mode are ignored.</summary>
    bool LiveMode { get; }

    Task<GatewayCheckoutSession> CreateCheckoutAsync(GatewayCheckoutRequest request, CancellationToken token);
    Task<GatewaySessionState> GetCheckoutAsync(string sessionId, CancellationToken token);
    /// <summary>Closes a session so it can no longer be paid.</summary>
    Task ExpireCheckoutAsync(string sessionId, CancellationToken token);
    /// <summary>One payment as the gateway holds it, including what has been refunded -- how a refund
    /// whose webhook never arrived is found.</summary>
    Task<GatewayPaymentState> GetPaymentAsync(string paymentId, CancellationToken token);
    /// <summary>Gives <paramref name="amount"/> of a payment back to the payer. Throws
    /// <see cref="PaymentGatewayException"/> when the gateway refuses (over the balance, a method that
    /// can't be refunded, ...).</summary>
    Task<GatewayRefund> RefundPaymentAsync(string paymentId, decimal amount, GatewayRefundReason reason, string? notes, CancellationToken token);

    /// <summary>The event in <paramref name="rawBody"/>, or null when its signature doesn't check
    /// out (or it can't be read) -- then it didn't come from the gateway.</summary>
    GatewayWebhookEvent? ReadWebhook(string rawBody, string? signatureHeader);
}

/// <summary>The gateway couldn't be reached or refused the request. <see cref="Exception.Message"/>
/// is for the logs; the owner is shown a generic message.</summary>
public class PaymentGatewayException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Null Object: what <see cref="IPaymentGateway"/> resolves to while online payment isn't configured.
/// The billing screen hides Pay now; a webhook that arrives anyway is rejected.
/// </summary>
public sealed class DisabledPaymentGateway : IPaymentGateway
{
    public string Provider => "none";
    public bool IsEnabled => false;
    public bool LiveMode => false;

    public Task<GatewayCheckoutSession> CreateCheckoutAsync(GatewayCheckoutRequest request, CancellationToken token) =>
        throw new PaymentGatewayException("Online payment isn't configured.");

    public Task<GatewaySessionState> GetCheckoutAsync(string sessionId, CancellationToken token) =>
        throw new PaymentGatewayException("Online payment isn't configured.");

    public Task ExpireCheckoutAsync(string sessionId, CancellationToken token) =>
        throw new PaymentGatewayException("Online payment isn't configured.");

    public Task<GatewayPaymentState> GetPaymentAsync(string paymentId, CancellationToken token) =>
        throw new PaymentGatewayException("Online payment isn't configured.");

    public Task<GatewayRefund> RefundPaymentAsync(string paymentId, decimal amount, GatewayRefundReason reason, string? notes, CancellationToken token) =>
        throw new PaymentGatewayException("Online payment isn't configured.");

    public GatewayWebhookEvent? ReadWebhook(string rawBody, string? signatureHeader) => null;
}
