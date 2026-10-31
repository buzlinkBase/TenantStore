using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities.Subs;

/// <summary>Where an owner's request to close the account stands. Stored as text.</summary>
public enum AccountClosureStatus
{
    /// <summary>Asked in the app; waiting for the owner to confirm from the email sent to them.</summary>
    PendingConfirmation,
    /// <summary>Confirmed; the owner can still change their mind until the cooling-off ends.</summary>
    CoolingOff,
    /// <summary>The cooling-off is over; waiting for an operator.</summary>
    ReadyForReview,
    /// <summary>An operator approved it: the account is locked and its data is scheduled for purge.</summary>
    Approved,
    Rejected,
    /// <summary>The owner withdrew it.</summary>
    Canceled,
    /// <summary>Never confirmed in time.</summary>
    Expired,
}

/// <summary>
/// An account owner's request, made in the app, to close the account. It deletes nothing by itself: the
/// owner confirms from their email, a cooling-off period runs (the owner can cancel at any time), and an
/// operator then approves, which locks the account and lets the usual purge-and-archive schedule run.
/// Gone with the account when an operator erases it for good.
/// </summary>
public class AccountClosureRequest : BaseEntity
{
    public Guid OwnerUserId { get; set; }
    /// <summary>The owner's email when asked (the confirmation is sent there).</summary>
    public string OwnerEmail { get; set; } = string.Empty;
    /// <summary>Optional: why the owner is leaving.</summary>
    public string? Reason { get; set; }
    public AccountClosureStatus RequestStatus { get; set; }
    public DateTime RequestedAtUtc { get; set; }

    /// <summary>SHA-256 of the one-time token in the confirmation link; the token itself is only ever in the email.</summary>
    public string ConfirmTokenHash { get; set; } = string.Empty;
    /// <summary>SHA-256 of the token in the "this wasn't me, cancel" link.</summary>
    public string CancelTokenHash { get; set; } = string.Empty;
    /// <summary>The confirmation link stops working after this.</summary>
    public DateTime ConfirmExpiresAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? CoolingOffEndsAtUtc { get; set; }

    public DateTime? DecidedAtUtc { get; set; }
    /// <summary>"platform:{email}" of the operator who approved or rejected it.</summary>
    public string? DecidedBy { get; set; }
    /// <summary>Why it was rejected; empty otherwise.</summary>
    public string? DecisionReason { get; set; }
}
