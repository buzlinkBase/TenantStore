namespace TenantStoreApi.Domain.Entities;

public class Tenant : BaseEntity
{
    public required string CompanyName { get; set; }
    public required string Email { get; set; }
    public string Token { get; set; } = Guid.NewGuid().ToString();
}

public class ApiToken : BaseEntity
{
    public   string Description { get; set; }
    public TokenType  TokenType  { get; set; }
    public TokenExpirationType ExpirationType { get; set; }
    public string? Token { get; set; }
    public Guid? UserId { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public new TokenStatus Status { get; set; }
    public string? Remarks { get; set; }
}
