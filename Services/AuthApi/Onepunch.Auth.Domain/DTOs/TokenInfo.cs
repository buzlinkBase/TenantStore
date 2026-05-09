using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Onepunch.Auth.Domain.DTOs;

public class TokenInfo
{
    public Guid JTI  { get; set; }
    public Guid UserId { get; set; }
    public string? Email { get; set; }
    public Guid TenantId  { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public record LoginPayload
{
    [EmailAddress]
    public required string Email { get; set; }
    public required string Password { get; set; }
}

public record LoginResponse
{
    public string ErrorMessage { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    //public Guid? DefaultTenantId { get; set; }
    //public string? DefaultTenantName  { get; set; }
    public List<UsersTenant> Tenants  { get; set; }
    public DateTime Expiry { get; set; } 
}

public class UsersTenant
{
    public Guid TenantId { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }

}

public class CreateWorkspaceRequest
{
    public string TenantName  { get; set; } = string.Empty;
}