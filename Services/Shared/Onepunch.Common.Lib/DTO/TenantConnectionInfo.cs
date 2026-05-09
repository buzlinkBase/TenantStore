namespace Onepunch.Common.Lib;

public class TenantConnectionInfo
{
    public string? ConnectionString { get; set; }
    public Guid TenantId { get; set; } = Guid.NewGuid();
}