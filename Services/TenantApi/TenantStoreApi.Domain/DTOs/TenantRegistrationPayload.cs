namespace TenantStoreApi.Domain.DTOs;



public class UpdateTenant
{
    public string Status { get; set; }
}

public class UpdateTenantProfile
{
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? Address { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? Contact { get; set; }
}

public class TenantModel
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TenantName { get; set; }
    public string Status { get; set; }
}

