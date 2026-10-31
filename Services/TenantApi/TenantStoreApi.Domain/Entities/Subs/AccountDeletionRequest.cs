using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities.Subs;

/// <summary>Stored as text.</summary>
public enum AccountDeletionRequestStatus
{
    /// <summary>Waiting for a second operator.</summary>
    Pending,
    /// <summary>A second operator approved it; the erasure is running (or done).</summary>
    Approved,
    Rejected,
    /// <summary>Withdrawn by the operator who asked.</summary>
    Canceled,
    /// <summary>Nobody decided in time.</summary>
    Expired,
}

/// <summary>Where an approved company erasure stands. Stored as text. (A whole account's progress is
/// on its subscription's PurgeState; the row is gone once the erasure is done.)</summary>
public enum CompanyErasureStatus
{
    /// <summary>Asked the services holding the company's data; waiting for hrms-api to report back.</summary>
    Running,
    /// <summary>hrms-api reported a failure; an operator can retry.</summary>
    Failed,
}

/// <summary>
/// One operator's request to erase an account -- or, with <see cref="TenantId"/> set, one of its
/// companies -- for good (a test account or company, a duplicate sign-up). Two people are needed: the
/// request does nothing until a DIFFERENT operator approves it. On approval the account (or company) is
/// erased everywhere, this row with it; a rejected, canceled or expired request stays as history until
/// the account itself goes.
/// </summary>
public class AccountDeletionRequest : BaseEntity
{
    public Guid OwnerUserId { get; set; }
    /// <summary>Set when only this company is to be erased; null for the whole account.</summary>
    public Guid? TenantId { get; set; }
    /// <summary>The company name typed to confirm (company requests only).</summary>
    public string? CompanyName { get; set; }
    /// <summary>Progress of an approved company erasure; null otherwise.</summary>
    public CompanyErasureStatus? Erasure { get; set; }
    /// <summary>Why the last attempt failed, as hrms-api reported it.</summary>
    public string? ErasureError { get; set; }
    /// <summary>The owner email typed to confirm, kept so the approver sees exactly what was asked for.</summary>
    public string OwnerEmail { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    /// <summary>The requesting operator ticked "email the owner when it's done": a short confirmation goes to
    /// <see cref="OwnerEmail"/> once the erasure has completed.</summary>
    public bool SendConfirmationEmail { get; set; }
    public AccountDeletionRequestStatus RequestStatus { get; set; }

    /// <summary>The requesting operator's id (token <c>sub</c>) -- what "a different operator" is checked against.</summary>
    public Guid RequestedById { get; set; }
    /// <summary>"platform:{email}" of the requesting operator.</summary>
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public Guid? DecidedById { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    /// <summary>Why it was rejected; empty for approvals and cancellations.</summary>
    public string? DecisionReason { get; set; }
}
