using OnePunch.Auth.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
