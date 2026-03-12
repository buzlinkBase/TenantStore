
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
    public string CompanyName { get; set; }
    public string Status { get; set; }
}

public class InvitationPayload
{
    public string? Name { get; set; } = "user";

    [Required]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public required string Email { get; set; }
}