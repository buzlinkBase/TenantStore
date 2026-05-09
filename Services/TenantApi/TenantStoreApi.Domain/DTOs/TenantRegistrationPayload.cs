
using System.ComponentModel.DataAnnotations;

namespace TenantStoreApi.Domain.DTOs;


 
public class UpdateTenant
{
    public string Status { get; set; }
}

public class TenantModel
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TenantName { get; set; }
    public string Status { get; set; }
}

