using BuzlinkRepository;
namespace TenantStoreApi.Domain.Entities;

public class BaseEntity : EntityBase
{
    public string Status { get; set; } = "Active";
}
