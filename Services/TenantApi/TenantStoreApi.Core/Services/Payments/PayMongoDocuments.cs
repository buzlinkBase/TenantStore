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

        var kind = type == CheckoutPaidEvent ? GatewayEventKind.CheckoutPaid : GatewayEventKind.Other;
        return new GatewayWebhookEvent(eventId, type, kind, IsTrue(attributes, "livemode"), session);
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
