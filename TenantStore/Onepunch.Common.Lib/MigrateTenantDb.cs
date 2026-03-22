namespace Onepunch.Common.Lib;
public class MigrateTenantDb
{
    public Guid TenantId { get; set; }
    public string System { get; set; }
    public string TargetVersion { get; set; }
    public string CurrentVersion { get; set; }
}

public class SchemaVersionUpdatePayload
{
    public Guid TenantId { get; set; }
    public string CurrentVersion { get; set; }
    public string System { get; set; }
    public string Status { get; set; } 
}