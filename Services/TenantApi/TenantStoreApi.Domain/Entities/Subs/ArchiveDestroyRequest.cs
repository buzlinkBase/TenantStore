using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities.Subs;

/// <summary>
/// One operator's request to destroy an account's archives before their retention date. Like an erasure it
/// needs a DIFFERENT operator to approve, and does nothing until then. Status reuses
/// <see cref="AccountDeletionRequestStatus"/>. Gone with the account when an operator erases it for good.
/// </summary>
public class ArchiveDestroyRequest : BaseEntity
{
    public Guid OwnerUserId { get; set; }
    /// <summary>The owner email typed to confirm, kept so the approver sees exactly what was asked for.</summary>
    public string OwnerEmail { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public AccountDeletionRequestStatus RequestStatus { get; set; }

    public Guid RequestedById { get; set; }
    /// <summary>"platform:{email}" of the requesting operator.</summary>
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public Guid? DecidedById { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    /// <summary>Why it was rejected; empty for approvals and withdrawals.</summary>
    public string? DecisionReason { get; set; }
}
