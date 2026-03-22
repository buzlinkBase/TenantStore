namespace Onepunch.Common.Lib.DTO; 
public class ConnectionStringPayload  
{
    public Guid TenantId { get; set; }
    /// <summary>
    /// The unique ID of the DigitalOcean Database Cluster (UUID)
    /// </summary>
    public string ClusterId { get; set; } = string.Empty;

    /// <summary>
    /// The specific database name (e.g., adms_tenant_1)
    /// </summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// The service that owns this DB (e.g., "hrms", "payroll")
    /// </summary>
    public string ServiceOwner { get; set; } = string.Empty;

    /// <summary>
    /// Tracking the current schema version applied to this tenant
    /// </summary>
    public string SchemaVersion { get; set; } = "0";

    /// <summary>
    /// The optimized .NET Connection String (with Max Pool Size=2)
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The raw URI format (mysql://user:pass@host:port/db)
    /// </summary>
    public string RawConnection { get; set; } = string.Empty;

    /// <summary>
    /// Production, Staging, or Development
    /// </summary>
    public string Environment { get; set; } = "Production";

    /// <summary>
    /// The specific module/sub-system identifier
    /// </summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// Master switch for tenant access
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Detailed status: Active, Migrating, Maintenance, Suspended
    /// </summary>
    public string Status { get; set; } = "Active";
}