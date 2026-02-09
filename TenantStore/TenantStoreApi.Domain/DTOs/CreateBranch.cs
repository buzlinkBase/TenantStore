using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TenantStoreApi.Domain.DTOs;

public class CreateBranch
{
    public required Guid TenantId { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Contact { get; set; }
    public string? ManagerName { get; set; }
}
public class UpdateBranch: CreateBranch
{
    public Guid Id { get; set; }
    public string Status { get; set; }
}

public class BranchModel  : UpdateBranch
{
}