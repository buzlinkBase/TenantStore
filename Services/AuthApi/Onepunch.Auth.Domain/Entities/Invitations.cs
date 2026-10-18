using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Domain.Entities;

/// <summary>What an invitation is for. HRIS accept/preview/login flows only ever see Staff ones.</summary>
public static class InvitationCategory
{
    /// <summary>Joins a company's HRIS (employees, admins, client contacts' HRIS logins).</summary>
    public const string Staff = "Staff";

    /// <summary>Gives a person access to one client in the client portal; never an HRIS membership.</summary>
    public const string ClientUser = "ClientUser";
}

public class Invitation : BaseEntity
{
    /// <summary>Staff or ClientUser -- see <see cref="InvitationCategory"/>. Existing rows are Staff.</summary>
    public string Category { get; set; } = InvitationCategory.Staff;

    /// <summary>The client a ClientUser invitation is for (Tenant Service's client entity id).</summary>
    public Guid? ClientEntityId { get; set; }

    /// <summary>The client's name when the invitation was sent, for the accept page and the email.</summary>
    public string? ClientName { get; set; }

    /// <summary>The roles (Tenant Service role ids) a ClientUser invitation grants within the client.</summary>
    public List<Guid> ClientRoleIds { get; set; } = new();

    public Guid UserId { get; set; }//sending the invites
    public Guid TenantId { get; set; }
    public string TenantName { get; set; }
    public string Email { get; set; }//invited
    public string Token { get; set; }
    public DateTime Expiry { get; set; }
    public InvitationStatus Status { get; set; }
    public List<string> Roles { get; set; } = new() { "None" };
    public Guid? EmployeeId { get; set; }

    /// <summary>The invitee's full name, when the inviter supplied one (typically from a linked
    /// Employee record) -- lets the accept-invite page pre-fill the Full Name field instead of
    /// the invitee retyping their own name. See InvitationService.SendUserInvitationAsync.</summary>
    public string? Name { get; set; }

    /// <summary>The client this login is for, when invited as a client (hrms-api Client.Id).</summary>
    public Guid? ClientId { get; set; }
}

/// <summary>
/// One batch-invite submission, keyed by the caller's X-Idempotency-Key: a retry of the same
/// request (network hiccup, double submit) returns the stored result instead of sending again.
/// </summary>
public class InvitationBatch : BaseEntity
{
    public Guid TenantId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid InvitedByUserId { get; set; }
    /// <summary>The <c>BatchInvitationResult</c> returned for this submission, as JSON.</summary>
    public string ResultJson { get; set; } = string.Empty;
}

public enum InvitationStatus
{
    Pending,
    Accepted,
}