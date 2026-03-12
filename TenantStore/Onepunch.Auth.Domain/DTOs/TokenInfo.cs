namespace Onepunch.Auth.Domain.DTOs;

public class TokenInfo
{
    public Guid UserId { get; set; }
    public string? Email { get; set; }
    public Guid DefaultTenantId { get; set; }
    public string TenantName  { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public record LoginPayload
{
    public string Email { get; set; }
    public string Password { get; set; }
}

public record LoginResponse
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public Guid? DefaultTenantId { get; set; }
    public List<UsersTenant> Tenants  { get; set; }
    public DateTime Expiry { get; set; } 
}

public class UsersTenant
{
    public Guid TenantId { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }

}