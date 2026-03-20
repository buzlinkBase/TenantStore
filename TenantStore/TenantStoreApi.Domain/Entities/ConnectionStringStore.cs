namespace TenantStoreApi.Domain.Entities;

public class ConnectionStringStore : BaseEntity
{
    public Guid TenantId { get; set; }
    public string ServiceOwner { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string ConnetionString { get; set; } = string.Empty;
    public string RawConnection { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Module { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
