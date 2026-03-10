
using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities;
public class Tenant : BaseEntity, IUserId
{
    public required string CompanyName { get; set; }
    public Guid UserId { get; set; }
}
