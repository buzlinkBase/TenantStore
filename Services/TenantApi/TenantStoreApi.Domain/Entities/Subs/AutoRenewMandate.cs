using Onepunch.AuditEngine;

namespace TenantStoreApi.Domain.Entities.Subs;

/// <summary>What an auto renew pays for: the plan, or the storage blocks (billed apart from the plan).</summary>
public enum AutoRenewTarget
{
    Plan,
    Storage,
}

public enum AutoRenewMandateStatus
{
    /// <summary>Set up at the gateway; waiting for the owner to pay the first invoice (which saves the card or wallet).</summary>
    Pending,
    /// <summary>Paid up: the gateway charges each cycle on its own.</summary>
    Active,
    /// <summary>The gateway's last charge failed; it is retrying.</summary>
    PastDue,
    /// <summary>Stopped -- by the owner, an operator, a failed first payment, or retries running out.</summary>
    Canceled,
}

/// <summary>
/// The owner's standing instruction to renew one target automatically: a subscription at the payment
/// gateway (PayMongo Subscriptions) that charges the saved card or Maya account every cycle. Our own
/// billing stays the source of truth -- each paid gateway invoice is recorded as an ordinary renewal,
/// and each draft invoice is corrected to our price before it is charged. At most one live
/// (non-<see cref="AutoRenewMandateStatus.Canceled"/>) mandate per subscription and target.
/// </summary>
[Audited]
public class AutoRenewMandate : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public Guid OwnerUserId { get; set; }
    public AutoRenewTarget Target { get; set; }
    public AutoRenewMandateStatus Status { get; set; }

    /// <summary>"paymongo".</summary>
    public string Provider { get; set; } = string.Empty;
    public string ProviderCustomerId { get; set; } = string.Empty;
    public string ProviderSubscriptionId { get; set; } = string.Empty;
    /// <summary>The gateway plan it bills on: its interval matches <see cref="BillingCycle"/>.</summary>
    public string ProviderPlanId { get; set; } = string.Empty;
    public BillingCycle BillingCycle { get; set; }

    /// <summary>What it charges, as the owner should recognize it: "Visa •••• 4242", "Maya".</summary>
    public string? PaymentMethodLabel { get; set; }
    /// <summary>The checkout that pays the first invoice.</summary>
    public Guid? SetupCheckoutId { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    /// <summary>The next charge date the gateway last reported.</summary>
    public DateTime? NextChargeAtUtc { get; set; }

    public DateTime? CanceledAtUtc { get; set; }
    public string? CancelReason { get; set; }
    public string? CanceledBy { get; set; }
    public DateTime? LastFailureAtUtc { get; set; }
    public string? LastFailureReason { get; set; }
    public string? CreatedBy { get; set; }

    public bool IsLive => Status != AutoRenewMandateStatus.Canceled;
}
