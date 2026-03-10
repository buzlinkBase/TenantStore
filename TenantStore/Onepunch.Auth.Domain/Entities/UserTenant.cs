using OnePunch.Auth.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace Onepunch.Auth.Domain.Entities;

public class UserTenant:BaseEntity
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
}
