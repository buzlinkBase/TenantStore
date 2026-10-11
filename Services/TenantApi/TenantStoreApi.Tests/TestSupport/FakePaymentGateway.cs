using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Tests.TestSupport;

/// <summary>
/// An in-memory payment gateway: sessions it opened, which of them are paid, and webhook deliveries
/// (a body is authentic when sent with the header <see cref="ValidSignature"/>).
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public const string ValidSignature = "valid";

    public string Provider => "fake";
    public bool IsEnabled { get; set; } = true;
    public bool LiveMode { get; set; }
    public bool FailNextCreate { get; set; }

    public List<GatewayCheckoutRequest> Created { get; } = [];
    public List<string> Expired { get; } = [];
    public Dictionary<string, GatewaySessionState> Sessions { get; } = new();
    public Dictionary<string, GatewayWebhookEvent> Deliveries { get; } = new();

    public Task<GatewayCheckoutSession> CreateCheckoutAsync(GatewayCheckoutRequest request, CancellationToken token)
    {
        if (FailNextCreate)
        {
            FailNextCreate = false;
            throw new PaymentGatewayException("payment_method_types: gcash is not activated");
        }
        Created.Add(request);
        var id = $"cs_{Created.Count}";
        Sessions[id] = new GatewaySessionState(id, null, request.Metadata, LiveMode);
        return Task.FromResult(new GatewayCheckoutSession(id, $"https://pay.test/{id}"));
    }

    public Task<GatewaySessionState> GetCheckoutAsync(string sessionId, CancellationToken token) =>
        Task.FromResult(Sessions[sessionId]);

    public Task ExpireCheckoutAsync(string sessionId, CancellationToken token)
    {
        Expired.Add(sessionId);
        return Task.CompletedTask;
    }

    /// <summary>What has been refunded per gateway payment id, as the gateway would report it.</summary>
    public Dictionary<string, decimal> Refunds { get; } = new();
    /// <summary>What each gateway payment was for, as the gateway would report it.</summary>
    public Dictionary<string, decimal> Amounts { get; } = new();
    public bool FailPaymentLookup { get; set; }

    public Task<GatewayPaymentState> GetPaymentAsync(string paymentId, CancellationToken token)
    {
        if (FailPaymentLookup) throw new PaymentGatewayException("PayMongo couldn't be reached.");
        return Task.FromResult(new GatewayPaymentState(paymentId, Amounts.GetValueOrDefault(paymentId), Refunds.GetValueOrDefault(paymentId)));
    }

    public List<(string PaymentId, decimal Amount, GatewayRefundReason Reason, string? Notes)> RefundRequests { get; } = [];
    public bool FailNextRefund { get; set; }

    public Task<GatewayRefund> RefundPaymentAsync(string paymentId, decimal amount, GatewayRefundReason reason, string? notes, CancellationToken token)
    {
        if (FailNextRefund)
        {
            FailNextRefund = false;
            throw new PaymentGatewayException("PayMongo refused POST refunds (400): this payment method can't be refunded");
        }
        RefundRequests.Add((paymentId, amount, reason, notes));
        Refunds[paymentId] = Refunds.GetValueOrDefault(paymentId) + amount;
        return Task.FromResult(new GatewayRefund($"rf_{RefundRequests.Count}", paymentId, amount, "pending"));
    }

    public GatewayWebhookEvent? ReadWebhook(string rawBody, string? signatureHeader) =>
        signatureHeader == ValidSignature ? Deliveries.GetValueOrDefault(rawBody) : null;

    /// <summary>The payer pays session <paramref name="sessionId"/>.</summary>
    public GatewaySessionState Pay(string sessionId, decimal amount, string methodName = "gcash", PaymentMethod method = PaymentMethod.GCash)
    {
        var session = Sessions[sessionId] with
        {
            Payment = new GatewayPayment($"pay_{sessionId}", amount, "PHP", method, methodName, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc)),
        };
        Sessions[sessionId] = session;
        Amounts[$"pay_{sessionId}"] = amount;
        return session;
    }

    /// <summary>A refund or dispute delivery against gateway payment <paramref name="paymentId"/>; returns its body.</summary>
    public string DeliverIssue(string paymentId, GatewayEventKind kind, decimal? amount = null, string eventId = "evt_issue")
    {
        Deliveries[eventId] = new GatewayWebhookEvent(
            eventId, kind == GatewayEventKind.PaymentRefunded ? "payment.refunded" : "payment.dispute.opened", kind, LiveMode, null,
            new GatewayPaymentIssue(paymentId, amount));
        return eventId;
    }

    /// <summary>A checkout_session.payment.paid delivery for <paramref name="session"/>; returns its body.</summary>
    public string Deliver(GatewaySessionState session, string eventId = "evt_1", bool? liveMode = null)
    {
        Deliveries[eventId] = new GatewayWebhookEvent(
            eventId, PayMongoDocuments.CheckoutPaidEvent, GatewayEventKind.CheckoutPaid, liveMode ?? LiveMode, session);
        return eventId;
    }
}
