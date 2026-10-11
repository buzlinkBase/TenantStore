namespace TenantStoreApi.Domain.Entities.Subs;

/// <summary>
/// A subscription plan an account can be on (Timekeeping, Payroll, Premium). What it unlocks is
/// data -- its <see cref="Entitlements"/> -- never inferred from <see cref="Code"/> or the name.
/// Prices are per plan, not per employee. Not [Audited]: the plan catalog is the platform's, not
/// any company's -- its edits are recorded as BillingEvents instead.
/// </summary>
public class Plan : BaseEntity
{
    /// <summary>Stable identifier, e.g. "timekeeping", "payroll", "premium".</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>Highlighted on the plan selection screen.</summary>
    public bool IsRecommended { get; set; }
    /// <summary>Offered on the plan selection screen. Off for retired or internal plans.</summary>
    public bool IsPublic { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal AnnualPrice { get; set; }
    public string Currency { get; set; } = "PHP";
    /// <summary>Whether the prices above already include 12% VAT -- shown on the plan screen.</summary>
    public bool PricesIncludeVat { get; set; } = true;
    /// <summary>An internal note for operators (what a custom plan is for). Shown only in the platform
    /// console -- never part of <c>PlanView</c>, so it never reaches a customer.</summary>
    public string? Remarks { get; set; }

    public virtual ICollection<PlanEntitlement> Entitlements { get; set; } = new List<PlanEntitlement>();
}

/// <summary>
/// A private plan offered to one subscriber (account owner). A plan that isn't public is seen on
/// the plan selection screen only by the owners it is offered to. Revoking an offer doesn't move
/// an owner already on the plan.
/// </summary>
public class PlanOffer : BaseEntity
{
    public Guid PlanId { get; set; }
    public virtual Plan Plan { get; set; } = null!;
    public Guid OwnerUserId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>One entitlement a plan grants: a feature flag ("true"/"false") or a limit (an
/// integer, -1 = unlimited). Keys are <c>Onepunch.Common.Lib.Subscriptions.EntitlementKeys</c>.</summary>
public class PlanEntitlement : BaseEntity
{
    public Guid PlanId { get; set; }
    public virtual Plan Plan { get; set; } = null!;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
