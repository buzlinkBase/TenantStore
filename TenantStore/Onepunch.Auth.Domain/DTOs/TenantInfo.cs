using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Onepunch.Auth.Domain.DTOs;

public class TenantInfo
{
    public Guid Id { get; set; } 
    public string CompanyName { get; set; }
}
