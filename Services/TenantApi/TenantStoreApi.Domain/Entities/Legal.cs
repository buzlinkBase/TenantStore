using BuzlinkRepository;

namespace TenantStoreApi.Domain.Entities;

public static class LegalDocumentTypes
{
    public const string Terms = "terms";
}

public static class LegalDocumentStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Superseded = "Superseded";
}

/// <summary>
/// One version of a legal document (the Terms of Service), written and published from the Platform
/// Admin Console. At most one Draft per type. Publishing freezes the text with its tokens resolved
/// (<see cref="RenderedMarkdown"/>) and numbers it, so the version a company accepted never changes
/// afterwards -- a later edit is a new Draft. Platform data: shared by every account, never erased
/// with one. The lifecycle lives in <see cref="LegalStatus"/>, not BaseEntity.Status ("Active").
/// </summary>
[DisableSoftDelete]
public class LegalDocument : BaseEntity
{
    public string DocType { get; set; } = LegalDocumentTypes.Terms;
    public string LegalStatus { get; set; } = LegalDocumentStatuses.Draft;

    /// <summary>1, 2, 3 ... per type, given on publish. Null while a draft.</summary>
    public int? Version { get; set; }

    /// <summary>What the operator wrote: Markdown with {{tokens}}.</summary>
    public string BodyMarkdown { get; set; } = string.Empty;

    /// <summary>The published text with every token filled in. Null while a draft.</summary>
    public string? RenderedMarkdown { get; set; }

    /// <summary>What changed from the previous version, shown to the people asked to accept it.</summary>
    public string? ChangeSummary { get; set; }

    /// <summary>On: companies must accept this version again. Off for a wording fix that changes nothing.</summary>
    public bool RequiresReacceptance { get; set; } = true;

    public DateTime? EffectiveAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public string? PublishedBy { get; set; }
}

/// <summary>
/// A company accepting a version of the Terms, through one of its owners or admins (at company setup,
/// or when a new version asks for it). Append-only evidence: never edited. Erased with the company.
/// </summary>
[DisableSoftDelete]
public class TermsAcceptance : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? UserName { get; set; }
    public int Version { get; set; }
    public DateTime AcceptedAtUtc { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
