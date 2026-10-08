using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Payments;

/// <summary>A hosted payment page to open for one quoted amount.</summary>
public sealed record GatewayCheckoutRequest
{
    public required Guid CheckoutId { get; init; }
    /// <summary>The line the payer sees, e.g. "Payroll plan (monthly)".</summary>
    public required string ItemName { get; init; }
    public required string Description { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string SuccessUrl { get; init; }
    public required string CancelUrl { get; init; }
    /// <summary>Echoed back on the session and its webhook -- how a payment finds its checkout.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

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

public enum GatewayEventKind
{
    CheckoutPaid,
    Other,
}

/// <summary>An authentic webhook delivery. <see cref="Session"/> is set for checkout events.</summary>
public sealed record GatewayWebhookEvent(
    string EventId,
    string Type,
    GatewayEventKind Kind,
    bool LiveMode,
    GatewaySessionState? Session);

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

    public GatewayWebhookEvent? ReadWebhook(string rawBody, string? signatureHeader) => null;
}
