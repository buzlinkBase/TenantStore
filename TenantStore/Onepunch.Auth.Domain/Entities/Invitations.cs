using BuzlinkRepository;
using OnePunch.Auth.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Onepunch.Auth.Domain.Entities;

public class Invitation : BaseEntity, IEntityTenant
{

    public Guid UserId { get; set; }//sending the invites
    public Guid TenantId { get; set; }
    public string Email { get; set; }//invited
    public string Token { get; set; }
    public DateTime Expiry { get; set; }

}
