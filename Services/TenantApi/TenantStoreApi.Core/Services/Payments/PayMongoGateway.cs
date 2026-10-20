using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Payments;

/// <summary>
/// Adapter: <see cref="IPaymentGateway"/> over PayMongo's Checkout Sessions API
/// (https://api.paymongo.com/v1/checkout_sessions), and <see cref="IRecurringBillingGateway"/> over its
/// Customers, Subscriptions and Payment Intents APIs. The HttpClient arrives configured with the base
/// address and the secret key as HTTP Basic auth -- see PaymentRegistrations. Every transport or API
/// failure surfaces as <see cref="PaymentGatewayException"/>.
/// </summary>
public sealed class PayMongoGateway : IPaymentGateway, IRecurringBillingGateway
{
    public const string ProviderName = "paymongo";

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly PayMongoOptions _options;

    public PayMongoGateway(HttpClient http, IOptions<PaymentOptions> options)
    {
        _http = http;
        _options = options.Value.PayMongo;
    }

    public string Provider => ProviderName;
    public bool IsEnabled => true;
    public bool LiveMode => _options.LiveMode;

    public async Task<GatewayCheckoutSession> CreateCheckoutAsync(GatewayCheckoutRequest request, CancellationToken token)
    {
        var lineItems = request.LineItems is { Count: > 0 }
            ? request.LineItems
            : [new GatewayCheckoutLineItem(request.ItemName, request.Amount, request.Description)];
        var body = new
        {
            data = new
            {
                attributes = new
                {
                    line_items = lineItems.Select(item => new
                        {
                            name = item.Name,
                            amount = PayMongoDocuments.ToCentavos(item.Amount),
                            currency = request.Currency,
                            quantity = 1,
                            description = item.Description,
                        }).ToArray(),
                    payment_method_types = _options.EffectivePaymentMethodTypes,
                    success_url = request.SuccessUrl,
                    cancel_url = request.CancelUrl,
                    reference_number = request.CheckoutId.ToString("N"),
                    description = request.Description,
                    // A JSON null in appsettings binds as "", which PayMongo refuses ("cannot be blank"):
                    // blank means unset, so it's left out and the account's default descriptor applies.
                    statement_descriptor = string.IsNullOrWhiteSpace(_options.StatementDescriptor)
                        ? null
                        : _options.StatementDescriptor.Trim(),
                    send_email_receipt = true,
                    show_description = true,
                    show_line_items = true,
                    metadata = request.Metadata,
                },
            },
        };

        var data = RequireResource(await SendAsync(HttpMethod.Post, "checkout_sessions", body, token));
        var id = data.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
        var url = data.TryGetProperty("attributes", out var attributes) && attributes.TryGetProperty("checkout_url", out var urlValue)
            ? urlValue.GetString()
            : null;
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url))
            throw new PaymentGatewayException("PayMongo created a checkout session without an id or checkout_url.");
        return new GatewayCheckoutSession(id, url);
    }

    public async Task<GatewaySessionState> GetCheckoutAsync(string sessionId, CancellationToken token) =>
        PayMongoDocuments.ReadSession(RequireResource(
            await SendAsync(HttpMethod.Get, $"checkout_sessions/{Uri.EscapeDataString(sessionId)}", null, token)));

    public Task ExpireCheckoutAsync(string sessionId, CancellationToken token) =>
        SendAsync(HttpMethod.Post, $"checkout_sessions/{Uri.EscapeDataString(sessionId)}/expire", null, token);

    public async Task<GatewayPaymentState> GetPaymentAsync(string paymentId, CancellationToken token) =>
        PayMongoDocuments.ReadPayment(RequireResource(
            await SendAsync(HttpMethod.Get, $"payments/{Uri.EscapeDataString(paymentId)}", null, token)));

    public async Task<GatewayRefund> RefundPaymentAsync(
        string paymentId, decimal amount, GatewayRefundReason reason, string? notes, CancellationToken token)
    {
        var body = new
        {
            data = new
            {
                attributes = new
                {
                    payment_id = paymentId,
                    amount = PayMongoDocuments.ToCentavos(amount),
                    reason = PayMongoDocuments.RefundReason(reason),
                    notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                },
            },
        };
        return PayMongoDocuments.ReadRefund(RequireResource(await SendAsync(HttpMethod.Post, "refunds", body, token)));
    }

    // ── Recurring billing (PayMongo Subscriptions) ──────────────────────────────────────────

    public string? PublicKey => string.IsNullOrWhiteSpace(_options.PublicKey) ? null : _options.PublicKey.Trim();

    public async Task<string> CreateCustomerAsync(GatewayCustomerRequest customer, CancellationToken token)
    {
        var body = new
        {
            data = new
            {
                attributes = new
                {
                    first_name = Clip(customer.FirstName, 255),
                    last_name = Clip(customer.LastName, 255),
                    email = customer.Email,
                    phone = string.IsNullOrWhiteSpace(customer.Phone) ? null : Clip(customer.Phone, 13),
                    default_device = "email",
                },
            },
        };
        return RequireId(await SendAsync(HttpMethod.Post, "customers", body, token), "customer");
    }

    public async Task<string> CreatePlanAsync(string name, string description, decimal amount, string currency, BillingCycle cycle,
        IReadOnlyDictionary<string, string> metadata, CancellationToken token)
    {
        var body = new
        {
            data = new
            {
                attributes = new
                {
                    name,
                    description,
                    amount = PayMongoDocuments.ToCentavos(amount),
                    currency,
                    interval = PayMongoDocuments.Interval(cycle),
                    interval_count = 1,
                    metadata,
                },
            },
        };
        return RequireId(await SendAsync(HttpMethod.Post, "subscriptions/plans", body, token), "plan");
    }

    public async Task<GatewayBillingSubscription> CreateSubscriptionAsync(string customerId, string planId, CancellationToken token)
    {
        var body = new { data = new { attributes = new { customer_id = customerId, plan_id = planId } } };
        var created = PayMongoDocuments.ReadSubscription(RequireResource(await SendAsync(HttpMethod.Post, "subscriptions", body, token)));
        if (string.IsNullOrEmpty(created.SubscriptionId))
            throw new PaymentGatewayException("PayMongo created a subscription without an id.");
        // The create response may leave out the first invoice's payment intent; a read has it.
        return created.PaymentIntentId != null ? created : await GetSubscriptionAsync(created.SubscriptionId, token);
    }

    public async Task<GatewayBillingSubscription> GetSubscriptionAsync(string subscriptionId, CancellationToken token) =>
        PayMongoDocuments.ReadSubscription(RequireResource(
            await SendAsync(HttpMethod.Get, $"subscriptions/{Uri.EscapeDataString(subscriptionId)}", null, token)));

    public Task CancelSubscriptionAsync(string subscriptionId, CancellationToken token) =>
        SendAsync(HttpMethod.Delete, $"subscriptions/{Uri.EscapeDataString(subscriptionId)}", null, token);

    public Task ChangeSubscriptionPlanAsync(string subscriptionId, string planId, CancellationToken token) =>
        SendAsync(HttpMethod.Put, $"subscriptions/{Uri.EscapeDataString(subscriptionId)}/plan",
            new { data = new { attributes = new { plan_id = planId } } }, token);

    public Task AddInvoiceLineItemAsync(string invoiceId, decimal amount, string description, CancellationToken token) =>
        SendAsync(HttpMethod.Post, $"subscriptions/invoices/{Uri.EscapeDataString(invoiceId)}/line_items",
            new { data = new { attributes = new { amount = PayMongoDocuments.ToCentavos(amount), description } } }, token);

    public async Task<string> CreateWalletPaymentMethodAsync(string type, string name, string email, CancellationToken token)
    {
        var body = new { data = new { attributes = new { type, billing = new { name, email } } } };
        return RequireId(await SendAsync(HttpMethod.Post, "payment_methods", body, token), "payment method");
    }

    public async Task<GatewayIntentAttach> AttachPaymentMethodAsync(string intentId, string paymentMethodId, string returnUrl, CancellationToken token) =>
        PayMongoDocuments.ReadAttach(RequireResource(await SendAsync(HttpMethod.Post,
            $"payment_intents/{Uri.EscapeDataString(intentId)}/attach",
            new { data = new { attributes = new { payment_method = paymentMethodId, return_url = returnUrl } } }, token)));

    public async Task<GatewayIntentState> GetPaymentIntentAsync(string intentId, CancellationToken token) =>
        PayMongoDocuments.ReadIntent(RequireResource(
            await SendAsync(HttpMethod.Get, $"payment_intents/{Uri.EscapeDataString(intentId)}", null, token)));

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];

    private static string RequireId(JsonElement data, string what) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } text
            ? text
            : throw new PaymentGatewayException($"PayMongo created a {what} without an id.");

    public GatewayWebhookEvent? ReadWebhook(string rawBody, string? signatureHeader)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            return PayMongoWebhookSignature.IsValid(rawBody, signatureHeader, _options.WebhookSecret, PayMongoDocuments.IsLiveEvent(root))
                ? PayMongoDocuments.ReadEvent(root)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement RequireResource(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty("attributes", out _)
            ? data
            : throw new PaymentGatewayException("PayMongo answered without a resource.");

    /// <summary>Sends a request and returns the response document's "data" (undefined when it has none).</summary>
    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body, options: Json);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, token);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException($"PayMongo couldn't be reached ({method} {path}).", ex);
        }
        catch (TaskCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new PaymentGatewayException($"PayMongo didn't answer in time ({method} {path}).", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                throw new PaymentGatewayException(
                    $"PayMongo refused {method} {path} ({(int)response.StatusCode}): {PayMongoDocuments.ErrorDetail(text) ?? "no detail"}");
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                return document.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default;
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException($"PayMongo answered {method} {path} with unreadable JSON.", ex);
            }
        }
    }
}
