using Onepunch.AuditEngine;

namespace TenantStoreApi.Domain.Entities.Subs;

/// <summary>
/// A purchasable block of extra storage (e.g. 50 GB for 400 PHP a month) the platform offers on top
/// of a plan's base storage. Like a plan, it is either public -- every account sees it -- or private,
/// seen only by the owners it is offered to (<see cref="StorageAddOnOffer"/>). The platform console
/// edits these; code never keys off <see cref="Code"/>. Not [Audited]: the catalog is the
/// platform's -- its edits are recorded as BillingEvents.
/// </summary>
public class StorageAddOnOffering : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>Storage one block adds, in MB (51,200 = 50 GB).</summary>
    public int BlockMb { get; set; }
    public decimal MonthlyPrice { get; set; }
    /// <summary>Per block for a year, billed up front by accounts on the annual cycle -- set below
    /// 12 x <see cref="MonthlyPrice"/> to give annual accounts a discount.</summary>
    public decimal AnnualPrice { get; set; }
    public string Currency { get; set; } = "PHP";
    public bool PricesIncludeVat { get; set; } = true;
    /// <summary>Offered to every account. Off: only to the owners it is offered to.</summary>
    public bool IsPublic { get; set; }
    /// <summary>Off: withdrawn from sale. Blocks already held keep working and renewing.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>A private storage offering made to one subscriber (account owner). Revoking it does not
/// remove blocks the owner already holds.</summary>
public class StorageAddOnOffer : BaseEntity
{
    public Guid OfferingId { get; set; }
    public virtual StorageAddOnOffering Offering { get; set; } = null!;
    public Guid OwnerUserId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>Where an account's storage billing period stands -- apart from its plan's.</summary>
public enum StorageSubscriptionStatus
{
    /// <summary>Paid up to <see cref="StorageSubscription.CurrentPeriodEndUtc"/>.</summary>
    Active,
    /// <summary>The period ended unpaid: the blocks still count until <see cref="StorageSubscription.GraceEndsAtUtc"/>.</summary>
    PastDue,
    /// <summary>Unpaid past the grace (or canceled): paid blocks no longer count. Paying restarts it.</summary>
    Lapsed,
}

/// <summary>What a storage payment paid for.</summary>
public enum StorageAddOnPaymentKind
{
    /// <summary>Blocks bought, for the rest of the storage period (or a new one).</summary>
    Purchase,
    /// <summary>Every paid block for one more storage period.</summary>
    Renewal,
}

/// <summary>
/// The account's storage billing: its own cycle and due date, independent of the plan's -- storage
/// can be paid monthly on an annual plan, or the reverse, and lapses on its own. One per account,
/// created with the first block bought (or granted). All dates UTC.
/// </summary>
[Audited]
public class StorageSubscription : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public virtual AccountSubscription Subscription { get; set; } = null!;
    public StorageSubscriptionStatus Status { get; set; }
    public BillingCycle BillingCycle { get; set; }
    /// <summary>The cycle the next renewal switches to.</summary>
    public BillingCycle? PendingBillingCycle { get; set; }
    public DateTime? CurrentPeriodStartUtc { get; set; }
    public DateTime? CurrentPeriodEndUtc { get; set; }
    /// <summary>Set on entering PastDue: the blocks keep counting until then.</summary>
    public DateTime? GraceEndsAtUtc { get; set; }
    /// <summary>Every paid block is being removed at the period end: it lapses then, with no grace.</summary>
    public bool CancelAtPeriodEnd { get; set; }
    /// <summary>Optimistic concurrency, as on the plan subscription.</summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Blocks of extra storage an account holds. The blocks add <c>Blocks x BlockMb</c> to the account's
/// <c>storage.max_mb</c> -- one pool shared by all its companies -- while its storage period is paid
/// (<see cref="StorageSubscription"/>), and renew with that period, not the plan's. <see cref="BlockMb"/> and the unit prices are fixed when
/// bought, so a later catalog edit doesn't reprice what a customer already pays for. A reduction is
/// scheduled (<see cref="PendingBlocks"/>) and applied at the end of the paid period.
/// </summary>
[Audited]
public class StorageAddOn : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public virtual AccountSubscription Subscription { get; set; } = null!;
    public Guid OfferingId { get; set; }
    public virtual StorageAddOnOffering Offering { get; set; } = null!;

    public int Blocks { get; set; }
    public int BlockMb { get; set; }
    /// <summary>Price of one block for a month (storage on the monthly cycle).</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>Price of one block for a year (storage on the annual cycle).</summary>
    public decimal UnitAnnualPrice { get; set; }
    /// <summary>Granted by the platform at no charge.</summary>
    public bool IsComplimentary { get; set; }

    /// <summary>Blocks to hold from <see cref="PendingEffectiveAtUtc"/> on; 0 cancels them.</summary>
    public int? PendingBlocks { get; set; }
    public DateTime? PendingEffectiveAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>A storage payment: blocks bought (<see cref="StorageAddOnPaymentKind.Purchase"/>) or one more
/// storage period (<see cref="StorageAddOnPaymentKind.Renewal"/>). Kept apart from SubscriptionPayment,
/// which defines the plan's paid period; this one only ever moves the storage period.</summary>
[Audited]
public class StorageAddOnPayment : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    /// <summary>Stored as text; existing rows are purchases.</summary>
    public StorageAddOnPaymentKind Kind { get; set; } = StorageAddOnPaymentKind.Purchase;
    /// <summary>The storage cycle it was priced in.</summary>
    public BillingCycle BillingCycle { get; set; }
    /// <summary>Purchases: the add-on the blocks went to. Renewals: <see cref="Guid.Empty"/> (they cover them all).</summary>
    public Guid AddOnId { get; set; }
    /// <summary>Purchases: blocks bought. Renewals: paid blocks renewed.</summary>
    public int Blocks { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PHP";
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public DateTime PaidAtUtc { get; set; }
    /// <summary>The span the amount pays for: a purchase, to the storage period's end; a renewal, the new period.</summary>
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public string? RecordedBy { get; set; }
    /// <summary>Item amounts captured when it was paid; null for purchases and older records.</summary>
    public string? LineItemsJson { get; set; }

    /// <summary>Set when an operator voids it (never deleted). A voided renewal hands its period back.</summary>
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
    public VoidKind? VoidKind { get; set; }
}
