namespace TenantStoreApi.Domain.Entities;
public class TenantConnection
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Module { get; set; } = "";
    public string ConnetionString { get; set; } = string.Empty;
}
