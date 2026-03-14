namespace TenantStoreApi.Domain.Entities; 
// 1. The "Menu" - What you sell (e.g., "Basic", "Pro", "Enterprise")
public class Plan : BaseEntity
{
    public string Name { get; set; } // e.g., "Premium HR Bundle"
    public string Description { get; set; }
    // Which services are included in this Plan?
    public virtual ICollection<PlanProduct> IncludedServices { get; set; }

}

// 2. The Link between Plan and ServiceType
public class PlanProduct : BaseEntity
{
    public Guid PlanId { get; set; }
    public ServiceType ServiceType { get; set; }
}
public class ExtraService : BaseEntity
{
    public string Name { get; set; }
}
// 3. The "Receipt" - The actual active sub for a specific Tenant
public class TenantSubscription : BaseEntity
{
    public Guid TenantId { get; set; }
    public virtual Tenant Tenant { get; set; }
    public Guid PlanId { get; set; }
    public virtual Plan Plan { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public SubscriptionStatus  SubStatus  { get; set; }
    // Useful for your workflow: Track if they bought extra "add-ons" 
    // outside of their standard Plan.
    public virtual ICollection<ExtraService> AddOns { get; set; }
}
public enum ServiceType
{
    None = 0,
    HR = 1,
    Accounting = 2,
    Payroll = 3,
    Inventory = 4
}
public enum SubscriptionStatus
{
    Active,
    Expired,
    Trialing,
    PastDue
}