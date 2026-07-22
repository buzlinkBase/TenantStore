namespace TenantStoreApi.Domain.DTOs;

public class CreateTenantDelegation
{
    public Guid HostTenantId { get; set; }
    public Guid GuestTenantId { get; set; }
    public string GuestTenantName { get; set; }
    public string AccessLevel { get; set; } = "";
}
