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

/// <summary>
/// Blocks of extra storage one company holds. The blocks add <c>Blocks x BlockMb</c> to the
/// company's <c>storage.max_mb</c> for as long as the account's plan is running, and renew with the
/// plan's billing period. <see cref="BlockMb"/> and <see cref="UnitPrice"/> are fixed when bought, so
/// a later catalog edit doesn't reprice what a customer already pays for. A reduction is scheduled
/// (<see cref="PendingBlocks"/>) and applied at the end of the paid period.
/// </summary>
[Audited]
public class StorageAddOn : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public virtual AccountSubscription Subscription { get; set; } = null!;
    /// <summary>The company the storage belongs to.</summary>
    public Guid TenantId { get; set; }
    public Guid OfferingId { get; set; }
    public virtual StorageAddOnOffering Offering { get; set; } = null!;

    public int Blocks { get; set; }
    public int BlockMb { get; set; }
    /// <summary>Monthly price of one block.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>Granted by the platform at no charge.</summary>
    public bool IsComplimentary { get; set; }

    /// <summary>Blocks to hold from <see cref="PendingEffectiveAtUtc"/> on; 0 cancels them.</summary>
    public int? PendingBlocks { get; set; }
    public DateTime? PendingEffectiveAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>A payment for storage blocks bought mid-period. Kept apart from SubscriptionPayment, which
/// defines the paid plan period and is rolled back when voided; this one never moves the period.</summary>
[Audited]
public class StorageAddOnPayment : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public Guid TenantId { get; set; }
    public Guid AddOnId { get; set; }
    public int Blocks { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PHP";
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public DateTime PaidAtUtc { get; set; }
    /// <summary>The (prorated) span the amount pays for: from the purchase to the plan period's end.</summary>
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public string? RecordedBy { get; set; }
}
