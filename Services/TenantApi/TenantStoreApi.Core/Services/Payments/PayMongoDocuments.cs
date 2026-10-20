using System.Text.Json;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Payments;

/// <summary>
/// Reads PayMongo's JSON:API documents -- checkout sessions, webhook events, errors -- into the
/// gateway records. Amounts are integers in centavos on the wire.
/// </summary>
public static class PayMongoDocuments
{
    public const string CheckoutPaidEvent = "checkout_session.payment.paid";

    public static long ToCentavos(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    public static decimal FromCentavos(long centavos) => centavos / 100m;

    /// <summary>A checkout_session resource (the "data" of a GET, or an event's attributes.data).</summary>
    public static GatewaySessionState ReadSession(JsonElement resource)
    {
        var id = Text(resource, "id") ?? string.Empty;
        if (resource.ValueKind != JsonValueKind.Object
            || !resource.TryGetProperty("attributes", out var attributes)
            || attributes.ValueKind != JsonValueKind.Object)
        {
            return new GatewaySessionState(id, null, new Dictionary<string, string>(), false);
        }

        GatewayPayment? paid = null;
        if (attributes.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Array)
        {
            foreach (var payment in payments.EnumerateArray())
            {
                if (!payment.TryGetProperty("attributes", out var p) || Text(p, "status") != "paid") continue;
                var methodName = SourceType(p) ?? Text(attributes, "payment_method_used");
                paid = new GatewayPayment(
                    Text(payment, "id") ?? string.Empty,
                    p.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number ? FromCentavos(amount.GetInt64()) : 0m,
                    Text(p, "currency") ?? "PHP",
                    MapMethod(methodName),
                    methodName,
                    p.TryGetProperty("paid_at", out var paidAt) && paidAt.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeSeconds(paidAt.GetInt64()).UtcDateTime
                        : null);
                break;
            }
        }

        return new GatewaySessionState(id, paid, ReadMetadata(attributes), IsTrue(attributes, "livemode"));
    }

    /// <summary>A payment resource: its amount, and the sum of its refunds that haven't failed
    /// (<c>attributes.refunds</c>, each with an <c>attributes.amount</c> and <c>status</c>).</summary>
    public static GatewayPaymentState ReadPayment(JsonElement resource)
    {
        var id = Text(resource, "id") ?? string.Empty;
        if (!resource.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
            return new GatewayPaymentState(id, 0m, 0m);

        var amount = attributes.TryGetProperty("amount", out var value) && value.ValueKind == JsonValueKind.Number
            ? FromCentavos(value.GetInt64())
            : 0m;
        var refunded = 0m;
        if (attributes.TryGetProperty("refunds", out var refunds) && refunds.ValueKind == JsonValueKind.Array)
        {
            foreach (var refund in refunds.EnumerateArray())
            {
                if (!refund.TryGetProperty("attributes", out var r) || Text(r, "status") == "failed") continue;
                if (r.TryGetProperty("amount", out var refundAmount) && refundAmount.ValueKind == JsonValueKind.Number)
                    refunded += FromCentavos(refundAmount.GetInt64());
            }
        }
        return new GatewayPaymentState(id, amount, refunded);
    }

    /// <summary>PayMongo's wire name for a refund reason.</summary>
    public static string RefundReason(GatewayRefundReason reason) => reason switch
    {
        GatewayRefundReason.RequestedByCustomer => "requested_by_customer",
        GatewayRefundReason.Duplicate => "duplicate",
        GatewayRefundReason.Fraudulent => "fraudulent",
        _ => "others",
    };

    /// <summary>A refund resource (the "data" of POST /refunds).</summary>
    public static GatewayRefund ReadRefund(JsonElement resource)
    {
        var id = Text(resource, "id") ?? string.Empty;
        resource.TryGetProperty("attributes", out var attributes);
        var amount = attributes.ValueKind == JsonValueKind.Object
            && attributes.TryGetProperty("amount", out var value) && value.ValueKind == JsonValueKind.Number
                ? FromCentavos(value.GetInt64())
                : 0m;
        return new GatewayRefund(id, Text(attributes, "payment_id") ?? string.Empty, amount, Text(attributes, "status") ?? "pending");
    }

    /// <summary>A webhook delivery's body, or null when it isn't a PayMongo event.</summary>
    public static GatewayWebhookEvent? ReadEvent(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("attributes", out var attributes)
            || attributes.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var eventId = Text(data, "id");
        var type = Text(attributes, "type");
        if (eventId == null || type == null) return null;

        GatewaySessionState? session = null;
        if (attributes.TryGetProperty("data", out var resource)
            && resource.ValueKind == JsonValueKind.Object
            && Text(resource, "type") == "checkout_session")
        {
            session = ReadSession(resource);
        }

        var kind = KindOf(type);
        GatewayPaymentIssue? issue = null;
        if (kind is GatewayEventKind.PaymentRefunded or GatewayEventKind.PaymentDisputed
            && attributes.TryGetProperty("data", out var issueResource))
        {
            issue = ReadIssue(issueResource);
        }

        GatewayInvoice? invoice = null;
        GatewaySubscriptionNotice? notice = null;
        if (attributes.TryGetProperty("data", out var billingResource) && billingResource.ValueKind == JsonValueKind.Object)
        {
            if (kind is GatewayEventKind.SubscriptionInvoiceCreated or GatewayEventKind.SubscriptionInvoicePaid
                or GatewayEventKind.SubscriptionInvoiceFailed)
            {
                invoice = ReadInvoice(billingResource);
            }
            else if (kind == GatewayEventKind.SubscriptionChanged)
            {
                var subscription = ReadSubscription(billingResource);
                notice = new GatewaySubscriptionNotice(subscription.SubscriptionId, subscription.Status, subscription.NextBillingDateUtc);
            }
        }
        return new GatewayWebhookEvent(eventId, type, kind, IsTrue(attributes, "livemode"), session, issue, invoice, notice);
    }

    // Recurring billing (PayMongo Subscriptions) events.
    public const string InvoiceCreatedEvent = "subscription.invoice.created";
    public const string InvoicePaidEvent = "subscription.invoice.paid";
    public const string InvoiceFailedEvent = "subscription.invoice.payment_failed";

    private static GatewayEventKind KindOf(string type)
    {
        if (type == CheckoutPaidEvent) return GatewayEventKind.CheckoutPaid;
        if (type == InvoiceCreatedEvent) return GatewayEventKind.SubscriptionInvoiceCreated;
        if (type == InvoicePaidEvent) return GatewayEventKind.SubscriptionInvoicePaid;
        if (type == InvoiceFailedEvent) return GatewayEventKind.SubscriptionInvoiceFailed;
        if (type.StartsWith("subscription.invoice.", StringComparison.Ordinal)) return GatewayEventKind.Other;
        if (type.StartsWith("subscription.", StringComparison.Ordinal)) return GatewayEventKind.SubscriptionChanged;
        if (type.StartsWith("payment.refund", StringComparison.Ordinal)) return GatewayEventKind.PaymentRefunded;
        if (type.Contains("dispute", StringComparison.Ordinal)) return GatewayEventKind.PaymentDisputed;
        return GatewayEventKind.Other;
    }

    /// <summary>The payment a refund or dispute event is about: the resource is the payment itself
    /// (<c>payment.refunded</c>) or a refund/dispute that names it in <c>payment_id</c>.</summary>
    private static GatewayPaymentIssue? ReadIssue(JsonElement resource)
    {
        if (resource.ValueKind != JsonValueKind.Object) return null;
        resource.TryGetProperty("attributes", out var attributes);
        var paymentId = Text(resource, "type") == "payment" ? Text(resource, "id") : Text(attributes, "payment_id");
        if (string.IsNullOrEmpty(paymentId)) return null;
        decimal? amount = attributes.ValueKind == JsonValueKind.Object
            && attributes.TryGetProperty("amount", out var value) && value.ValueKind == JsonValueKind.Number
                ? FromCentavos(value.GetInt64())
                : null;
        return new GatewayPaymentIssue(paymentId, amount);
    }

    /// <summary>Whether a webhook body says it's a live-mode event -- which signature to check.</summary>
    public static bool IsLiveEvent(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("attributes", out var attributes)
        && IsTrue(attributes, "livemode");

    /// <summary>The first error's detail from an error response, if it has one.</summary>
    public static string? ErrorDetail(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                return Text(first, "detail") ?? Text(first, "code");
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>PayMongo's payment source names to ours.</summary>
    public static PaymentMethod MapMethod(string? sourceType) => sourceType switch
    {
        "gcash" => PaymentMethod.GCash,
        "paymaya" => PaymentMethod.Maya,
        "card" => PaymentMethod.Card,
        "qrph" => PaymentMethod.QrPh,
        "grab_pay" => PaymentMethod.GrabPay,
        "shopee_pay" => PaymentMethod.ShopeePay,
        "billease" => PaymentMethod.BillEase,
        { } type when type.StartsWith("dob", StringComparison.Ordinal) || type.StartsWith("brankas", StringComparison.Ordinal)
            => PaymentMethod.OnlineBanking,
        _ => PaymentMethod.Other,
    };

    /// <summary>
    /// A subscription resource (a GET's data, or a webhook's attributes.data). Read from the resource's
    /// attributes, or the object itself when an event sends it flat.
    /// </summary>
    public static GatewayBillingSubscription ReadSubscription(JsonElement resource)
    {
        var a = AttributesOf(resource);
        string? invoiceId = null, invoiceStatus = null, intentId = null, intentStatus = null;
        if (a.TryGetProperty("latest_invoice", out var invoice) && invoice.ValueKind == JsonValueKind.Object)
        {
            invoiceId = Text(invoice, "id");
            invoiceStatus = Text(invoice, "status");
            if (invoice.TryGetProperty("payment_intent", out var intent) && intent.ValueKind == JsonValueKind.Object)
            {
                intentId = Text(intent, "id");
                intentStatus = Text(intent, "status");
            }
        }
        var planId = a.TryGetProperty("plan", out var plan) && plan.ValueKind == JsonValueKind.Object
            ? Text(plan, "id")
            : Text(a, "plan_id");
        return new GatewayBillingSubscription(
            Text(resource, "id") ?? Text(a, "id") ?? string.Empty,
            Text(a, "status") ?? string.Empty,
            Text(a, "customer_id"),
            planId,
            Date(a, "next_billing_schedule"),
            invoiceId, invoiceStatus, intentId, intentStatus);
    }

    /// <summary>A subscription invoice (a webhook's attributes.data). The subscription is named by
    /// <c>resource_id</c> or a nested <c>subscription.id</c>.</summary>
    public static GatewayInvoice ReadInvoice(JsonElement resource)
    {
        var a = AttributesOf(resource);
        var subscriptionId = Text(a, "resource_id")
            ?? (a.TryGetProperty("subscription", out var subscription) && subscription.ValueKind == JsonValueKind.Object
                ? Text(subscription, "id")
                : null);
        var intentId = a.TryGetProperty("payment_intent", out var intent) && intent.ValueKind == JsonValueKind.Object
            ? Text(intent, "id")
            : Text(a, "payment_intent_id");
        return new GatewayInvoice(
            Text(resource, "id") ?? Text(a, "id") ?? string.Empty,
            subscriptionId,
            a.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number ? FromCentavos(amount.GetInt64()) : 0m,
            Text(a, "currency") ?? "PHP",
            Text(a, "status") ?? string.Empty,
            Date(a, "due_date"),
            intentId);
    }

    /// <summary>A payment_intent resource: its status, the redirect a payer must follow, and its paid payment.</summary>
    public static GatewayIntentState ReadIntent(JsonElement resource)
    {
        var a = AttributesOf(resource);
        GatewayPayment? paid = null;
        string? label = null;
        if (a.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Array)
        {
            foreach (var payment in payments.EnumerateArray())
            {
                var p = AttributesOf(payment);
                if (Text(p, "status") != "paid") continue;
                var methodName = SourceType(p);
                paid = new GatewayPayment(
                    Text(payment, "id") ?? string.Empty,
                    p.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number ? FromCentavos(amount.GetInt64()) : 0m,
                    Text(p, "currency") ?? "PHP",
                    MapMethod(methodName),
                    methodName,
                    p.TryGetProperty("paid_at", out var paidAt) && paidAt.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeSeconds(paidAt.GetInt64()).UtcDateTime
                        : null);
                label = MethodLabel(p);
                break;
            }
        }
        return new GatewayIntentState(Text(resource, "id") ?? string.Empty, Text(a, "status") ?? string.Empty, paid, label, ReadMetadata(a));
    }

    /// <summary>The page a payer must visit to authorize an intent (3-D Secure, a wallet), if any.</summary>
    public static GatewayIntentAttach ReadAttach(JsonElement resource)
    {
        var a = AttributesOf(resource);
        string? url = null;
        if (a.TryGetProperty("next_action", out var next) && next.ValueKind == JsonValueKind.Object
            && next.TryGetProperty("redirect", out var redirect) && redirect.ValueKind == JsonValueKind.Object)
        {
            url = Text(redirect, "url");
        }
        return new GatewayIntentAttach(Text(a, "status") ?? string.Empty, url);
    }

    /// <summary>"Visa •••• 4242" for a card, "Maya" for a Maya wallet -- never more of the card than that.</summary>
    public static string? MethodLabel(JsonElement paymentAttributes)
    {
        if (!paymentAttributes.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object) return null;
        var type = Text(source, "type");
        if (type == "card")
        {
            var brand = Text(source, "brand");
            var last4 = Text(source, "last4");
            var name = string.IsNullOrWhiteSpace(brand) ? "Card" : char.ToUpperInvariant(brand[0]) + brand[1..];
            return last4 == null ? name : $"{name} •••• {last4}";
        }
        return type switch
        {
            "paymaya" => "Maya",
            null => null,
            _ => type,
        };
    }

    /// <summary>PayMongo's interval name for a billing cycle.</summary>
    public static string Interval(BillingCycle cycle) => cycle == BillingCycle.Annual ? "yearly" : "monthly";

    private static JsonElement AttributesOf(JsonElement resource) =>
        resource.ValueKind == JsonValueKind.Object
        && resource.TryGetProperty("attributes", out var attributes)
        && attributes.ValueKind == JsonValueKind.Object
            ? attributes
            : resource;

    /// <summary>A "YYYY-MM-DD" date, as midnight UTC.</summary>
    private static DateTime? Date(JsonElement element, string name) =>
        Text(element, name) is { } text
        && DateTime.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;

    private static string? SourceType(JsonElement paymentAttributes) =>
        paymentAttributes.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object
            ? Text(source, "type")
            : null;

    private static Dictionary<string, string> ReadMetadata(JsonElement attributes)
    {
        var metadata = new Dictionary<string, string>();
        if (!attributes.TryGetProperty("metadata", out var values) || values.ValueKind != JsonValueKind.Object) return metadata;
        foreach (var property in values.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String) metadata[property.Name] = property.Value.GetString()!;
        }
        return metadata;
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;
}
