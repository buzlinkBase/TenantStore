namespace TenantStoreApi.Domain.DTOs;

public class MemberResponse
{
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
}
