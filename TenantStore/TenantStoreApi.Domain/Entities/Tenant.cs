namespace TenantStoreApi.Domain.Entities;
public class Tenant : BaseEntity, IUserId
{
    public  string? TenantName { get; set; }
    public Guid UserId { get; set; }
}
