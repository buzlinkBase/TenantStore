namespace Onepunch.Auth.Domain.DTOs;

public class CreateToken
{
    public required string Description { get; set; }
    public TokenType TokenType { get; set; } = TokenType.Api;
    public TokenExpirationType ExpirationType { get; set; } = TokenExpirationType.X1Mos;
    public DateTime? ExpireAt { get; set; }
    public Guid? TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string Email { get; set; }
}

public class ApiTokenModel
{
    public string? Token { get; set; }
    public DateTime? ExpireAt { get; set; }
}
