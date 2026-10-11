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

/// <summary>Where a locked account's data is in its removal: kept, archived and deleted from the live
/// databases a year after the lock, and finally the archive itself destroyed (DOLE 3 yrs, BIR 5 yrs).</summary>
public enum DataPurgeState
{
    None,
    /// <summary>Locked, with a purge date set.</summary>
    Scheduled,
    /// <summary>Asked hrms-api to archive and delete; waiting for every company to report back.</summary>
    InProgress,
    /// <summary>Archived and removed from the live databases; the archive is kept until
    /// <see cref="AccountSubscription.ArchiveRetainUntilUtc"/>.</summary>
    Purged,
    /// <summary>The archive's retention ran out and it was destroyed. Nothing is left.</summary>
    ArchiveDestroyed,
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
    /// <summary>Set once the account is read-only (canceled, expired or suspended): its data is
    /// archived and removed from the live databases after this.</summary>
    public DateTime? PurgeAfterUtc { get; set; }
    public DataPurgeState PurgeState { get; set; }
    /// <summary>When every company's data had been archived and removed.</summary>
    public DateTime? DataPurgedAtUtc { get; set; }
    /// <summary>The compliance archive is destroyed after this: 5 years from the last payroll /
    /// attendance / accounting entry.</summary>
    public DateTime? ArchiveRetainUntilUtc { get; set; }
    /// <summary>An operator's hold (pending tax audit, protest, refund claim, labor case): blocks
    /// both the purge and the archive's destruction until released.</summary>
    public bool LegalHold { get; set; }
    public string? LegalHoldReason { get; set; }

    /// <summary>The owner canceled: access continues to the end of the paid period.</summary>
    public bool CancelAtPeriodEnd { get; set; }
    public Guid? PendingPlanId { get; set; }
    public BillingCycle? PendingBillingCycle { get; set; }
    public PlanChangeTiming? PendingChangeTiming { get; set; }
    /// <summary>For <see cref="PlanChangeTiming.AtPeriodEnd"/>: the period end it was requested
    /// against. Kept separately because paying the next period moves CurrentPeriodEndUtc on.</summary>
    public DateTime? PendingChangeEffectiveAtUtc { get; set; }

    /// <summary>Whether this account is shown the public plans and storage offerings. Off for a client on a
    /// custom plan: they then see only what was offered to them (and the plan they hold), never the public
    /// catalog. On for everyone by default.</summary>
    public bool ShowPublicOffering { get; set; } = true;

    /// <summary>The prices this account pays, fixed when it subscribed -- a plan price change
    /// doesn't reach existing subscribers until a platform admin migrates them.</summary>
    public decimal? LockedMonthlyPrice { get; set; }
    public decimal? LockedAnnualPrice { get; set; }

    /// <summary>Optimistic concurrency: the hourly lifecycle job and a payment landing at the
    /// same moment must not both apply.</summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public virtual ICollection<SubscriptionOverride> Overrides { get; set; } = new List<SubscriptionOverride>();
    public virtual ICollection<StorageAddOn> StorageAddOns { get; set; } = new List<StorageAddOn>();
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

    /// <summary>Why it was voided. Null on payments voided before kinds existed, which count as
    /// <see cref="Subs.VoidKind.EntryError"/>: the owner never sees those.</summary>
    public VoidKind? VoidKind { get; set; }
}

/// <summary>Stored as text. Decides whether the owner still sees a voided payment: only
/// <see cref="EntryError"/> (recorded by mistake) is hidden from them; the others stay in their
/// billing history marked with what happened, because the money really moved.</summary>
public enum VoidKind
{
    /// <summary>The money was returned to the owner.</summary>
    Refunded,
    /// <summary>The payment fell through: cancelled, failed, bounced or charged back.</summary>
    Cancelled,
    /// <summary>Recorded by mistake (duplicate, wrong amount): it never happened as far as the owner is concerned.</summary>
    EntryError,
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
