namespace Onepunch.Auth.Domain.DTOs;

public class UserProfileResponse
{
    public Guid Id { get; set; }
    public string? DefaultTenantName { get; set; }
    public Guid? DefaultTenantId { get; set; }
    public List<string> DefaultTenantRoles { get; set; } = new();
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public string Status { get; set; } = string.Empty;
}
