namespace TenantStoreApi.Domain.Entities.Subs;

// 1. The "Menu" - What you sell (e.g., "Basic", "Pro", "Enterprise")
public class Plan : BaseEntity
{
    public string Name { get; set; } // e.g., "Premium HR Bundle"
    public string Description { get; set; }
    public int Days { get; set; }
    // Which services are included in this Plan?
    public virtual ICollection<PlanProduct> IncludedServices { get; set; }

}
