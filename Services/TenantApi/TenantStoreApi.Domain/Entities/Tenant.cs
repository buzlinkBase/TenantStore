using Onepunch.AuditEngine;

namespace TenantStoreApi.Domain.Entities;

[Audited(EntityType = "Company")]
public class Tenant : BaseEntity, IUserId
{
    public string? TenantName { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// When the company finished setting up (hrms-api's HrisOrgProvisioned with a success status --
    /// TenantActivatedWorker). Null while provisioning, or when setup failed or was abandoned. Only
    /// activated companies start the owner's trial and count as companies the account owns.
    /// </summary>
    public DateTime? ActivatedAtUtc { get; set; }

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