namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Flow B (Invited User Onboarding): published by Auth Service when an owner/admin sends an
/// invite, alongside (not gating) the notification-email publish, so Tenant Service can create
/// a pending membership row (Status="Invited") before the invitee ever accepts. On acceptance,
/// UserJoin (carrying the same Email) is matched against this placeholder to activate it.
/// </summary>
public record UserInvited
{
    public string Email { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string FullName  { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new() { "Member" };
    public Guid InvitedByUserId { get; set; }
    public string InvitationToken { get; set; } = string.Empty;
    public DateTime Expiry { get; set; }
}
