namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Published by Tenant Service once ONE company erased from the Platform Console (the account itself
/// stays) is gone from every company database and from Tenant Service. Auth Service finishes the job on
/// receipt: deletes the logins that existed only for this company, detaches the people who also belong
/// elsewhere (the owner always does), and writes the one operator log line that outlives the company.
/// Idempotent, like <see cref="AccountHardDeleted"/>.
/// </summary>
public record CompanyHardDeleted
{
    /// <summary>The deletion request's id -- also the dedupe key of the operator log line.</summary>
    public Guid RequestId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    /// <summary>Logins that belonged only to this company -- deleted outright.</summary>
    public List<Guid> DeleteUserIds { get; set; } = new();
    /// <summary>Logins that also belong elsewhere -- kept, with this company cleared as their default.</summary>
    public List<Guid> DetachUserIds { get; set; } = new();
    public string RequestedBy { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime DeletedAtUtc { get; set; } = DateTime.UtcNow;
}
