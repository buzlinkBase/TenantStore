using BuzlinkRepository;
using Onepunch.AuditEngine;

namespace TenantStoreApi.Domain.Entities.Subs;

public enum AccountSubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Suspended,
    Expired,
    Canceled,
}

public enum BillingCycle
{
    Monthly,
    Annual,
}

/// <summary>When a requested plan change takes effect.</summary>
public enum PlanChangeTiming
{
    /// <summary>Upgrades: as soon as the payment for the new plan is recorded.</summary>
    OnPayment,
    /// <summary>Downgrades: at the end of the period already paid for.</summary>
    AtPeriodEnd,
}

/// <summary>
/// An account's subscription. Belongs to the account (the owner <c>User</c>), not to a company:
/// every company whose <see cref="Tenant.UserId"/> is <see cref="OwnerUserId"/> gets its
/// entitlements from here. One per account -- which is also what limits it to one trial.
/// All dates UTC.
/// </summary>
[Audited(EntityType = "Subscription")]
public class AccountSubscription : BaseEntity
{
    public Guid OwnerUserId { get; set; }
    public Guid PlanId { get; set; }
    public virtual Plan Plan { get; set; } = null!;
    public AccountSubscriptionStatus SubStatus { get; set; }
    public BillingCycle BillingCycle { get; set; }

    public DateTime? TrialEndsAtUtc { get; set; }
    /// <summary>A platform admin may extend a trial once.</summary>
    public bool TrialExtended { get; set; }
    public DateTime? CurrentPeriodStartUtc { get; set; }
    public DateTime? CurrentPeriodEndUtc { get; set; }
    /// <summary>Set on entering PastDue: full access continues until then.</summary>
    public DateTime? GraceEndsAtUtc { get; set; }
    public DateTime? CanceledAtUtc { get; set; }
    /// <summary>Set on cancellation: company data may be purged after this (phase 2).</summary>
    public DateTime? PurgeAfterUtc { get; set; }

    /// <summary>The owner canceled: access continues to the end of the paid period.</summary>
    public bool CancelAtPeriodEnd { get; set; }
    public Guid? PendingPlanId { get; set; }
    public BillingCycle? PendingBillingCycle { get; set; }
    public PlanChangeTiming? PendingChangeTiming { get; set; }
    /// <summary>For <see cref="PlanChangeTiming.AtPeriodEnd"/>: the period end it was requested
    /// against. Kept separately because paying the next period moves CurrentPeriodEndUtc on.</summary>
    public DateTime? PendingChangeEffectiveAtUtc { get; set; }

    /// <summary>The prices this account pays, fixed when it subscribed -- a plan price change
    /// doesn't reach existing subscribers until a platform admin migrates them.</summary>
    public decimal? LockedMonthlyPrice { get; set; }
    public decimal? LockedAnnualPrice { get; set; }

    /// <summary>Optimistic concurrency: the hourly lifecycle job and a payment landing at the
    /// same moment must not both apply.</summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public virtual ICollection<SubscriptionOverride> Overrides { get; set; } = new List<SubscriptionOverride>();
}

/// <summary>
/// An exception to the plan for one account -- or one of its companies when
/// <see cref="TenantId"/> is set. Can revoke a feature the plan includes (Value "false"), grant
/// one it doesn't, or change a limit. Company overrides win over account overrides, which win
/// over the plan.
/// </summary>
[Audited]
public class SubscriptionOverride : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public virtual AccountSubscription Subscription { get; set; } = null!;
    /// <summary>Null: every company the account owns. Set: only this company.</summary>
    public Guid? TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? CreatedBy { get; set; }
}

[Audited]
public class SubscriptionDiscount : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public virtual AccountSubscription Subscription { get; set; } = null!;
    /// <summary>Either a percentage off (0-100) or a fixed amount off per billing period.</summary>
    public decimal? Percent { get; set; }
    public decimal? FixedAmount { get; set; }
    public string? Reason { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>Stored as text, so new values need no migration. The ones after <see cref="Other"/>
/// come from online checkout (PaymentCheckout), mapped from the gateway's payment source.</summary>
public enum PaymentMethod
{
    BankTransfer,
    Cash,
    Check,
    GCash,
    Maya,
    Card,
    Other,
    QrPh,
    GrabPay,
    ShopeePay,
    OnlineBanking,
    BillEase,
}

/// <summary>A payment for one billing period: recorded by a platform admin, or by the payment
/// gateway's webhook when an online checkout is paid (RecordedBy "paymongo").</summary>
[Audited]
public class SubscriptionPayment : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public virtual AccountSubscription Subscription { get; set; } = null!;
    public Guid PlanId { get; set; }
    public BillingCycle BillingCycle { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PHP";
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public string? RecordedBy { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Set when a platform admin voids a payment recorded by mistake (never deleted: the money trail
    /// stays). A voided payment no longer counts towards the paid period or the billing history the
    /// owner sees -- see PlatformAccountActionsService.VoidPaymentAsync.
    /// </summary>
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>
/// Billing history: every transition, plan change, payment, override and reminder sent.
/// <see cref="DedupeKey"/> (unique) makes repeated work idempotent -- a reminder already sent, or
/// (phase 2) a gateway webhook already processed, finds its key taken and is skipped.
/// Never deleted, so not soft-deletable either.
/// </summary>
[DisableSoftDelete]
public class BillingEvent : BaseEntity
{
    public Guid? SubscriptionId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? DedupeKey { get; set; }
    public string? Actor { get; set; }
}
