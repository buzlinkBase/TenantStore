using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities;

public class UserMembership : BaseEntity, IEntityTenant
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; }
}
