
using System.ComponentModel.DataAnnotations;

namespace TenantStoreApi.Domain.DTOs;

public record CreateTenant
{
    public required string CompanyName { get; set; }
     
}

//public record TenantRegistrationRequest : CreateTenant
//{
//    public Guid TenantId { get; set; }
//}

public class UpdateTenant
{
    public string Status { get; set; }
}

public class TenantModel
{
    public Guid Id { get; set; }
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