using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities.Subs;

public enum PaymentCheckoutStatus
{
    /// <summary>The hosted payment page is live; the owner may still pay.</summary>
    Open,
    Paid,
    /// <summary>Replaced by a newer checkout, or gone stale. Still settled if it turns out paid.</summary>
    Expired,
    /// <summary>The gateway refused to create it.</summary>
    Failed,
}

public enum PaymentCheckoutKind
{
    /// <summary>One period of a plan (<see cref="PaymentCheckout.PlanId"/> and cycle).</summary>
    Plan,
    /// <summary>Extra storage blocks for the account; BillingCycle is the storage cycle they're priced in
    /// (PlanId is the plan running at the time, for reference only).</summary>
    StorageAddOn,
    /// <summary>One more storage period for every paid block; BillingCycle is the storage cycle renewed into.</summary>
    StorageRenewal,
}

/// <summary>
/// One attempt to pay online: what was quoted (plan, cycle, amount) when the owner clicked Pay,
/// and the gateway's hosted checkout session it opened. When the gateway reports it paid, the
/// payment is recorded against the subscription (<see cref="SubscriptionPaymentId"/>) for exactly
/// this plan and cycle -- whatever has changed on the account since, the customer paid for this.
/// </summary>
public class PaymentCheckout : BaseEntity
{
    /// <summary>What is being bought. Stored as text; existing rows are plan checkouts.</summary>
    public PaymentCheckoutKind Kind { get; set; } = PaymentCheckoutKind.Plan;
    /// <summary>Storage checkouts: the offering and block count quoted.</summary>
    public Guid? StorageOfferingId { get; set; }
    public int? StorageBlocks { get; set; }
    /// <summary>Storage checkouts (purchase or renewal): the StorageAddOnPayment recorded when it was paid.</summary>
    public Guid? StorageAddOnPaymentId { get; set; }

    /// <summary>
    /// Set when this pays the first invoice of an auto renew (<see cref="AutoRenewMandate"/>): paid in the
    /// gateway's payment-intent flow rather than a hosted checkout page, so <see cref="ProviderSessionId"/>
    /// holds the gateway subscription and <see cref="ProviderIntentId"/> the intent.
    /// </summary>
    public Guid? MandateId { get; set; }
    public string? ProviderIntentId { get; set; }

    public Guid SubscriptionId { get; set; }
    public virtual AccountSubscription Subscription { get; set; } = null!;
    public Guid OwnerUserId { get; set; }

    /// <summary>"paymongo".</summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>The gateway's id for the hosted session (PayMongo: cs_...).</summary>
    public string? ProviderSessionId { get; set; }
    public string? CheckoutUrl { get; set; }
    public PaymentCheckoutStatus CheckoutStatus { get; set; }

    public Guid PlanId { get; set; }
    public BillingCycle BillingCycle { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PHP";
    /// <summary>The item amounts quoted to PayMongo, captured when this checkout was created.</summary>
    public string? LineItemsJson { get; set; }

    /// <summary>The user who opened it (owner or billing admin).</summary>
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>The gateway's id for the payment itself (PayMongo: pay_...).</summary>
    public string? ProviderPaymentId { get; set; }
    /// <summary>How it was paid, as the gateway names it (gcash, card, qrph, ...).</summary>
    public string? ProviderPaymentMethod { get; set; }
    public Guid? SubscriptionPaymentId { get; set; }
    public string? FailureReason { get; set; }
}
