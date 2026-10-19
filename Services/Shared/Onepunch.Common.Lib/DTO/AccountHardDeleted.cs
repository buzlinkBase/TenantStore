namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Published by Tenant Service once an account erased from the Platform Console is gone from every
/// company database and from Tenant Service itself. Auth Service finishes the job on receipt
/// (AccountHardDeletedWorker): deletes the logins that existed only for this account, detaches the
/// people who also belong elsewhere, and writes the one operator log line that outlives the account.
/// Idempotent: a redelivery finds nothing left to delete and does not write a second log line.
/// </summary>
public record AccountHardDeleted
{
    /// <summary>The deletion request's id -- also the dedupe key of the operator log line.</summary>
    public Guid RequestId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public List<Guid> TenantIds { get; set; } = new();
    public List<string> CompanyNames { get; set; } = new();
    /// <summary>Logins that belonged only to this account -- deleted outright.</summary>
    public List<Guid> DeleteUserIds { get; set; } = new();
    /// <summary>Logins that also belong to other accounts -- kept, with any default company that
    /// pointed at an erased one cleared.</summary>
    public List<Guid> DetachUserIds { get; set; } = new();
    /// <summary>Operator who asked for the deletion ("platform:{email}").</summary>
    public string RequestedBy { get; set; } = string.Empty;
    /// <summary>The second operator who approved it ("platform:{email}").</summary>
    public string ApprovedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime DeletedAtUtc { get; set; } = DateTime.UtcNow;
}
