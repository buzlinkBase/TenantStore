namespace TenantStoreApi.Domain.Entities;

public class TenantDelegation : BaseEntity
{
    public Guid HostTenantId { get; set; }
    public Guid GuestTenantId { get; set; }
    public string GuestTenantName  { get; set; }
    public string AccessLevel { get; set; } = "";
}
