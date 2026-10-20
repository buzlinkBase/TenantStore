using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Tests.TestSupport;

/// <summary>
/// An in-memory payment gateway: sessions it opened, which of them are paid, and webhook deliveries
/// (a body is authentic when sent with the header <see cref="ValidSignature"/>). Also its recurring
/// billing: customers, plans, subscriptions with their invoices and payment intents.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway, IRecurringBillingGateway
{
    // ── Recurring billing ────────────────────────────────────────────────────────────────────

    public sealed record FakePlan(string Id, decimal Amount, BillingCycle Cycle, string Name);

    public string? PublicKey { get; set; } = "pk_test_fake";
    public List<GatewayCustomerRequest> Customers { get; } = [];
    public Dictionary<string, FakePlan> Plans { get; } = new();
    public Dictionary<string, GatewayBillingSubscription> BillingSubscriptions { get; } = new();
    public Dictionary<string, GatewayIntentState> Intents { get; } = new();
    public List<(string InvoiceId, decimal Amount, string Description)> LineItems { get; } = [];
    public List<string> CancelledSubscriptions { get; } = [];
    public List<(string SubscriptionId, string PlanId)> PlanChanges { get; } = [];
    public List<(string IntentId, string PaymentMethodId, string ReturnUrl)> Attached { get; } = [];
    public List<string> WalletMethods { get; } = [];
    /// <summary>Off: attaching needs the payer to authorize it on a redirect page (3-D Secure, Maya).</summary>
    public bool AttachPaysImmediately { get; set; }
    /// <summary>The next billing date every new subscription reports.</summary>
    public DateTime? NextBillingDateUtc { get; set; }

    public Task<string> CreateCustomerAsync(GatewayCustomerRequest customer, CancellationToken token)
    {
        Customers.Add(customer);
        return Task.FromResult($"cus_{Customers.Count}");
    }

    public Task<string> CreatePlanAsync(string name, string description, decimal amount, string currency, BillingCycle cycle,
        IReadOnlyDictionary<string, string> metadata, CancellationToken token)
    {
        var id = $"plan_{Plans.Count + 1}";
        Plans[id] = new FakePlan(id, amount, cycle, name);
        return Task.FromResult(id);
    }

    public Task<GatewayBillingSubscription> CreateSubscriptionAsync(string customerId, string planId, CancellationToken token)
    {
        var n = BillingSubscriptions.Count + 1;
        var subscription = new GatewayBillingSubscription(
            $"subs_{n}", "incomplete", customerId, planId, NextBillingDateUtc, $"inv_{n}_1", "open", $"pi_{n}", "awaiting_payment_method");
        BillingSubscriptions[subscription.SubscriptionId] = subscription;
        Intents[$"pi_{n}"] = new GatewayIntentState($"pi_{n}", "awaiting_payment_method", null, null, new Dictionary<string, string>());
        return Task.FromResult(subscription);
    }

    public Task<GatewayBillingSubscription> GetSubscriptionAsync(string subscriptionId, CancellationToken token) =>
        Task.FromResult(BillingSubscriptions[subscriptionId]);

    public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken token)
    {
        CancelledSubscriptions.Add(subscriptionId);
        if (BillingSubscriptions.TryGetValue(subscriptionId, out var s)) BillingSubscriptions[subscriptionId] = s with { Status = "cancelled" };
        return Task.CompletedTask;
    }

    public Task ChangeSubscriptionPlanAsync(string subscriptionId, string planId, CancellationToken token)
    {
        PlanChanges.Add((subscriptionId, planId));
        return Task.CompletedTask;
    }

    public Task AddInvoiceLineItemAsync(string invoiceId, decimal amount, string description, CancellationToken token)
    {
        LineItems.Add((invoiceId, amount, description));
        return Task.CompletedTask;
    }

    public Task<string> CreateWalletPaymentMethodAsync(string type, string name, string email, CancellationToken token)
    {
        WalletMethods.Add(type);
        return Task.FromResult($"pm_wallet_{WalletMethods.Count}");
    }

    public Task<GatewayIntentAttach> AttachPaymentMethodAsync(string intentId, string paymentMethodId, string returnUrl, CancellationToken token)
    {
        Attached.Add((intentId, paymentMethodId, returnUrl));
        if (!AttachPaysImmediately)
            return Task.FromResult(new GatewayIntentAttach("awaiting_next_action", $"https://pay.test/authorize/{intentId}"));
        var subscription = BillingSubscriptions.Values.First(s => s.PaymentIntentId == intentId);
        PayIntent(intentId, Plans[subscription.PlanId!].Amount);
        return Task.FromResult(new GatewayIntentAttach("succeeded", null));
    }

    public Task<GatewayIntentState> GetPaymentIntentAsync(string intentId, CancellationToken token) =>
        Task.FromResult(Intents[intentId]);

    /// <summary>The payer completes intent <paramref name="intentId"/> (authorizes the card or Maya payment).</summary>
    public GatewayIntentState PayIntent(string intentId, decimal amount, string label = "Visa •••• 4242",
        PaymentMethod method = PaymentMethod.Card, string methodName = "card")
    {
        var intent = new GatewayIntentState(intentId, "succeeded",
            new GatewayPayment($"pay_{intentId}", amount, "PHP", method, methodName, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc)),
            label, new Dictionary<string, string>());
        Intents[intentId] = intent;
        Amounts[$"pay_{intentId}"] = amount;
        return intent;
    }

    /// <summary>A later cycle: the gateway raises invoice <paramref name="invoiceId"/> with its own payment intent.</summary>
    public GatewayInvoice RaiseInvoice(string subscriptionId, string invoiceId, decimal amount, string status = "draft")
    {
        var intentId = $"pi_{invoiceId}";
        Intents[intentId] = new GatewayIntentState(intentId, "awaiting_payment_method", null, null, new Dictionary<string, string>());
        BillingSubscriptions[subscriptionId] = BillingSubscriptions[subscriptionId] with
        {
            LatestInvoiceId = invoiceId, LatestInvoiceStatus = status, PaymentIntentId = intentId,
        };
        return new GatewayInvoice(invoiceId, subscriptionId, amount, "PHP", status, null, intentId);
    }

    /// <summary>A recurring-billing delivery about <paramref name="invoice"/>; returns its body.</summary>
    public string DeliverInvoice(GatewayEventKind kind, GatewayInvoice invoice, string? eventId = null)
    {
        var id = eventId ?? $"evt_{kind}_{invoice.InvoiceId}";
        var type = kind switch
        {
            GatewayEventKind.SubscriptionInvoiceCreated => PayMongoDocuments.InvoiceCreatedEvent,
            GatewayEventKind.SubscriptionInvoicePaid => PayMongoDocuments.InvoicePaidEvent,
            _ => PayMongoDocuments.InvoiceFailedEvent,
        };
        Deliveries[id] = new GatewayWebhookEvent(id, type, kind, LiveMode, null, null, invoice);
        return id;
    }

    /// <summary>A subscription.* status delivery; returns its body.</summary>
    public string DeliverSubscription(string subscriptionId, string status, string? eventId = null)
    {
        var id = eventId ?? $"evt_{subscriptionId}_{status}";
        Deliveries[id] = new GatewayWebhookEvent(id, $"subscription.{status}", GatewayEventKind.SubscriptionChanged, LiveMode, null,
            null, null, new GatewaySubscriptionNotice(subscriptionId, status, null));
        return id;
    }

    // ── Hosted checkout ──────────────────────────────────────────────────────────────────────

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
