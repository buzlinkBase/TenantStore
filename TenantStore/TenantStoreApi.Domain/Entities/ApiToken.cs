using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities;

public class ApiToken : BaseEntity, IEntityTenant
{
    public string Description { get; set; }
    public TokenType TokenType { get; set; }
    public TokenExpirationType ExpirationType { get; set; }
    public string? Token { get; set; }
    public Guid? UserId { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public new TokenStatus Status { get; set; }
    public string? Remarks { get; set; }
    public Guid TenantId { get; set; }
}
