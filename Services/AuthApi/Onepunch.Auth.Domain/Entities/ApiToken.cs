using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Domain.Entities;

public class ApiToken : BaseEntity
{
    public string Description { get; set; } = "";
    public TokenType TokenType { get; set; }
    public TokenExpirationType ExpirationType { get; set; }
    public string? Token { get; set; }
    public Guid? UserId { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public new TokenStatus Status { get; set; }
    public string? Remarks { get; set; }
    public Guid? TenantId { get; set; }
}


