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

    public virtual ICollection<PlanEntitlement> Entitlements { get; set; } = new List<PlanEntitlement>();

    // ── Legacy per-tenant model (TenantSubscription) -- unused; dropped with that table ─────
    public int Days { get; set; }
    public virtual ICollection<PlanProduct> IncludedServices { get; set; } = new List<PlanProduct>();
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
