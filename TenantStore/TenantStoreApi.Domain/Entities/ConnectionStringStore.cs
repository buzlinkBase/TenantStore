namespace TenantStoreApi.Domain.Entities;

public class ConnectionStringStore : BaseEntity
{
    public Guid TenantId { get; set; }
    public string ClusterId { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string ServiceOwner { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = "0";
    public string ConnectionString { get; set; } = string.Empty;
    public string Environment { get; set; } = "Production";
    public string Module { get; set; } = string.Empty; 
    public bool IsActive { get; set; } = true;

}