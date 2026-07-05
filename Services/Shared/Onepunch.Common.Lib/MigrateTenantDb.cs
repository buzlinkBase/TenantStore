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

public class DbStateUpdatePayload 
{
    public Guid TenantId { get; set; }
    public string ServiceOwner { get; set; }
    public DateTime? ArchieveSchedule   { get; set; }
    public bool IsActive  { get; set; }
    public string? Remarks   { get; set; }
    public string Status  { get; set; }
}

