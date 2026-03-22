namespace TenantStoreApi.Domain.Entities.Subs; 

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