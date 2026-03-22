namespace TenantStoreApi.Domain.Entities;

public class Tenant : BaseEntity, IUserId
{
    public string? TenantName { get; set; }
    public Guid UserId { get; set; }

    // Rename to plural for clarity
    public virtual ICollection<SchemaVersion> SchemaVersions { get; set; } = new List<SchemaVersion>();
}

public class SchemaVersion : BaseEntity
{
    public Guid TenantId { get; set; }
    // Navigation property back to Tenant
    public virtual Tenant Tenant { get; set; } = null!;
    // e.g., "HRIS", "Payroll", "Attendance"
    public string System { get; set; } = string.Empty;
    // The version currently installed in the DB
    public string CurrentVersion { get; set; } = string.Empty;
    // The version we are trying to reach during a deployment
    public string TargetVersion { get; set; } = string.Empty;
}