namespace TenantStoreApi.Domain.DTOs;

public class MemberResponse
{
    public Guid UserId { get; set; }
    public List<string> Roles { get; set; } = new();
}
