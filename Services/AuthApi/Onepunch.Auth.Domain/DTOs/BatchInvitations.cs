using System.Text.Json.Serialization;

namespace Onepunch.Auth.Domain.DTOs;

/// <summary>Who a batch invites: employees (each email tied to an employee) or logins for one client.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InviteEntityType
{
    Employee,
    Client,
}

/// <summary>What an email would be if invited now -- see InvitationService.CheckRecipientsAsync.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InviteRecipientStatus
{
    Ok,
    InvalidEmail,
    Member,
    PendingInvite,
}

public class BatchInvitationCheckRequest
{
    public List<string> Emails { get; set; } = [];
}

public class InviteRecipientCheck
{
    public string Email { get; set; } = string.Empty;
    public InviteRecipientStatus Status { get; set; }
    /// <summary>The member's name, for <see cref="InviteRecipientStatus.Member"/>.</summary>
    public string? Name { get; set; }
    /// <summary>When the still-valid invitation was sent, for <see cref="InviteRecipientStatus.PendingInvite"/>.</summary>
    public DateTime? SentAt { get; set; }
}

public class BatchInvitationRecipient
{
    public string Email { get; set; } = string.Empty;
    /// <summary>The employee this email belongs to (Employee batches).</summary>
    public Guid? EntityId { get; set; }
    public string? Name { get; set; }
}

public class BatchInvitationRequest
{
    public InviteEntityType EntityType { get; set; }
    /// <summary>The client every login in a Client batch is for.</summary>
    public Guid? ClientId { get; set; }
    public string Role { get; set; } = string.Empty;
    public List<BatchInvitationRecipient> Recipients { get; set; } = [];
}

public class FailedInvitation
{
    public string Email { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class BatchInvitationResult
{
    public int Sent { get; set; }
    public List<FailedInvitation> Failed { get; set; } = [];
}
