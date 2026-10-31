using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities.Subs;

/// <summary>
/// What a purge kept of one company: where its compliance archive is stored, how to check it, and when it
/// was destroyed. Written when hrms-api reports the purge (<c>data.purged</c>), so an operator can find a
/// purged company by name or owner without knowing its ids. The archive's contents are never held here.
/// Gone with the account or company when an operator erases it for good.
/// </summary>
public class PurgedCompanyArchive : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>The company's name when it was purged.</summary>
    public string CompanyName { get; set; } = string.Empty;
    /// <summary>The archive's storage key; null when the company had nothing to archive.</summary>
    public string? ArchiveKey { get; set; }
    public string? ArchiveSha256 { get; set; }
    public long RowsArchived { get; set; }
    public DateTime PurgedAtUtc { get; set; }
    /// <summary>Set once the archive has been destroyed.</summary>
    public DateTime? DestroyedAtUtc { get; set; }
}
