using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Domain.Entities;

public class TenantCreationRequestStatus : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public TenantCreationStatus Status { get; set; } = TenantCreationStatus.Pending;
}

public enum TenantCreationStatus
{
    Pending,
    Created
}
