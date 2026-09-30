using System.ComponentModel.DataAnnotations;

namespace Onepunch.Auth.Domain.DTOs;

public class InvitationRequest
{
    [Required]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public required string Email { get; set; }

    /// <summary>Empty means the default, Employee (InvitationPolicy.DefaultRoleFor).</summary>
    public List<string> Roles { get; set; } = new() { "Employee" };
    public Guid? EmployeeId { get; set; }
    /// <summary>The employee's full name, when this invite is tied to a linked Employee record --
    /// used to greet them by name in the invite email instead of falling back to their email
    /// address. See InvitationService.SendUserInvitationAsync.</summary>
    public string? Name { get; set; }

}