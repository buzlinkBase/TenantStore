namespace TenantStoreApi.Domain.Entities.Subs;

// 2. The Link between Plan and ServiceType
public class PlanProduct : BaseEntity
{
    public Guid PlanId { get; set; }
    public ServiceType ServiceType { get; set; }
}
