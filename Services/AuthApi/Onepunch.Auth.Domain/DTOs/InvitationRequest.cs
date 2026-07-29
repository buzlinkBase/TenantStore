using System.ComponentModel.DataAnnotations;

namespace Onepunch.Auth.Domain.DTOs;

public class InvitationRequest
{
    [Required]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public required string Email { get; set; }

    public List<string> Roles { get; set; } = new() { "Member" };
}