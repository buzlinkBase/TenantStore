namespace TenantStoreApi.Domain.Entities;
public class TenantConnection:BaseEntity
{
    public Guid TenantId { get; set; }
    public string Module { get; set; } = "";
    public string ConnetionString { get; set; } = string.Empty;
    public string service_owner { get; set; } = string.Empty;
    public string environment { get; set; } = string.Empty;
}
