using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Domain.Entities;

public class Invitation : BaseEntity
{
    public Guid UserId { get; set; }//sending the invites
    public Guid TenantId { get; set; }
    public string TenantName { get; set; }
    public string Email { get; set; }//invited
    public string Token { get; set; }
    public DateTime Expiry { get; set; }
    public InvitationStatus Status { get; set; }
    public List<string> Roles { get; set; } = new() { "Member" };
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