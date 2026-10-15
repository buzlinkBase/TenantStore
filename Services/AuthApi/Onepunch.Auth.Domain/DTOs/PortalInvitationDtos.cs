namespace Onepunch.Auth.Domain.DTOs;

// Wire contracts of client-user invitations (api/v1/portal/invitations). Like the sign-in, only the
// portal's server calls them; the acting person's id travels in the body because it is the portal
// that authenticated them.

/// <summary>Invites an email to one client with these roles (Tenant Service role ids). <see cref="InviterUserId"/> is who is inviting.</summary>
public record PortalInviteRequest(Guid InviterUserId, Guid ClientEntityId, string Email, string? FullName, List<Guid> RoleIds);

/// <summary>Resend or revoke: <see cref="ActingUserId"/> must be someone who could send this invitation.</summary>
public record PortalInvitationActionRequest(Guid ActingUserId);

public record PortalAcceptInvitationRequest(string Token, string? FullName, string? Password);

public record PortalInvitationView(
    Guid Id,
    Guid ClientEntityId,
    string Email,
    string? Name,
    List<Guid> RoleIds,
    DateTime ExpiresAtUtc,
    DateTime SentAtUtc);

/// <summary>What the accept page shows before asking for anything. <see cref="AccountExists"/>: they only need to confirm, not choose a password.</summary>
public record PortalInvitationPreview(
    string Email,
    string? Name,
    string ClientName,
    string CompanyName,
    bool Valid,
    bool AccountExists,
    DateTime ExpiresAtUtc);

/// <summary><see cref="AccountCreated"/> is false when the email already had an account, which keeps its password.</summary>
public record PortalAcceptInvitationResult(Guid AccountId, Guid ClientEntityId, bool AccountCreated);
