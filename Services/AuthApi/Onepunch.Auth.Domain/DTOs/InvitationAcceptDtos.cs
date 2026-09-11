using System.ComponentModel.DataAnnotations;

namespace Onepunch.Auth.Domain.DTOs;

/// <summary>
/// Flow B (diagram step 6 "Clicks invite link"): what the accept-invite landing page needs
/// to render before asking for credentials — who invited them, to which workspace, and
/// whether they already have an account (so the UI can offer "sign in" instead of "create").
/// </summary>
public class InvitationPreviewResponse
{
    public string Email { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public DateTime Expiry { get; set; }
    public bool Valid { get; set; }
    public bool AccountExists { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? Name { get; set; }
}

/// <summary>
/// Flow B (diagram steps 7-9): creates the invited user's account and accepts the invitation
/// in one step, for an email that has no existing account yet. Anonymous — the invite token
/// itself is the proof of email ownership.
/// </summary>
public class AcceptInvitationByTokenRequest
{
    [Required(ErrorMessage = "Invitation token is required")]
    public required string Token { get; set; }
    public string? Name { get; set; }
    [Required(ErrorMessage = "Password is required")]
    public required string Password { get; set; }
}

/// <summary>Body for the already-authenticated accept flow — was previously (incorrectly)
/// read as a query string parameter named "invitationToken" while the client sent a JSON
/// body {token: "..."}, so acceptance always failed.</summary>
public class AcceptInvitationRequest
{
    [Required(ErrorMessage = "Invitation token is required")]
    public required string Token { get; set; }
}
