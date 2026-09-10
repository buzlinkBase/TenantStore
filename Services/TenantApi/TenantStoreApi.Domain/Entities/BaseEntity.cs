using BuzlinkRepository;
namespace TenantStoreApi.Domain.Entities;

public class BaseEntity : EntityBase, ITimeStamp
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string Status { get; set; } = "Active";

}
