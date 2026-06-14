namespace Onepunch.Auth.Domain.DTOs;

public class TenantStatusResponse
{
    public Guid TenantId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsReady { get; set; }
}
