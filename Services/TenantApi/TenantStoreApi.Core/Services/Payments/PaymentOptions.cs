namespace TenantStoreApi.Core.Services.Payments;

/// <summary>
/// "Payments" configuration section. Online payment is on only when <see cref="ReturnBaseUrl"/> and
/// the PayMongo secret key are set (see PaymentRegistrations); otherwise owners see the manual
/// payment instructions alone.
/// </summary>
public sealed class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary>Where the web app is served (e.g. https://hris.onepunch.site): the hosted payment page
    /// sends the payer back to its /billing/payment screen.</summary>
    public string ReturnBaseUrl { get; set; } = string.Empty;

    /// <summary>Names the subscription on the payment page.</summary>
    public string ProductName { get; set; } = "One Punch HRIS";

    public PayMongoOptions PayMongo { get; set; } = new();
}

/// <summary>"Payments:PayMongo". Keys come from the PayMongo dashboard (Developers).</summary>
public sealed class PayMongoOptions
{
    public static readonly IReadOnlyList<string> DefaultPaymentMethodTypes = ["card", "gcash", "paymaya", "qrph"];

    public string ApiBaseUrl { get; set; } = "https://api.paymongo.com/v1/";
    /// <summary>sk_test_... or sk_live_... -- which one decides test or live mode.</summary>
    public string SecretKey { get; set; } = string.Empty;
    /// <summary>The webhook's signing secret (whsk_...), shown when the webhook is created.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
    /// <summary>
    /// What the payment page offers: card, gcash, paymaya, qrph, grab_pay, shopee_pay, billease,
    /// dob, dob_ubp, brankas_bdo, brankas_landbank, brankas_metrobank. Only methods activated on the
    /// PayMongo account work. Empty: <see cref="DefaultPaymentMethodTypes"/> (not a list initializer --
    /// the configuration binder would append to it).
    /// </summary>
    public List<string> PaymentMethodTypes { get; set; } = [];
    /// <summary>Shown on the payer's card statement. Unset: the PayMongo account's default.</summary>
    public string? StatementDescriptor { get; set; }

    /// <summary>The secret key is enough to take payments: without <see cref="WebhookSecret"/> every
    /// webhook is refused, and payments are confirmed only when the payer's return screen asks
    /// PayMongo (fine on a developer machine PayMongo can't reach; not for production).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
    public bool LiveMode => SecretKey.StartsWith("sk_live_", StringComparison.Ordinal);

    public IReadOnlyList<string> EffectivePaymentMethodTypes =>
        PaymentMethodTypes.Count > 0 ? PaymentMethodTypes : DefaultPaymentMethodTypes;
}
