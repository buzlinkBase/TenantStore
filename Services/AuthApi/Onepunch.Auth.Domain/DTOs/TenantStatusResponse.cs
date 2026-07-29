namespace Onepunch.Auth.Domain.DTOs;

public class TenantStatusResponse
{
    public Guid TenantId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsReady { get; set; }

    /// <summary>Raw status text from the external HRIS system (null until it reports in).</summary>
    public string? HrDbStatus { get; set; }
    /// <summary>True once the tenant's HR database has been provisioned and is safe to use.</summary>
    public bool HrDbReady { get; set; }
}
