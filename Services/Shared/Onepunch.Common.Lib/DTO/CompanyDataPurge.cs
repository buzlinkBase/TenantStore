namespace Onepunch.Common.Lib.DTO;

/// <summary>What <see cref="PurgeCompanyData"/> asks hrms-api to do for one company.</summary>
public enum CompanyPurgeStage
{
    /// <summary>One year after the account locked: write the compliance archive, verify it, then
    /// delete the company's rows and files from the live databases.</summary>
    ArchiveAndPurge,
    /// <summary>Once the 5-year legal retention has run out: delete the archive itself.</summary>
    DestroyArchive,
    /// <summary>An operator erased a (test) account from the Platform Console after a second operator
    /// approved it: delete everything for the company at once -- no read-only check, no archive, and
    /// the company row itself goes too. The company may still be live when this arrives.</summary>
    HardDelete,
}

/// <summary>
/// Published by Tenant Service (with the company in the X-Tenant-ID header) when a locked account's
/// data is due for removal. hrms-api re-checks the company is still read-only before it deletes
/// anything, and answers with <see cref="CompanyDataPurged"/>. Idempotent: a redelivery of an
/// already-purged company does nothing.
/// </summary>
public record PurgeCompanyData
{
    public Guid OwnerUserId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid TenantId { get; set; }
    public CompanyPurgeStage Stage { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Published by hrms-api when it has finished (or failed) one <see cref="PurgeCompanyData"/>.</summary>
public record CompanyDataPurged
{
    public Guid OwnerUserId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid TenantId { get; set; }
    public CompanyPurgeStage Stage { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    /// <summary>The company's latest payroll / attendance / accounting entry -- the start of the
    /// 5-year retention clock. Null when it never had any.</summary>
    public DateTime? LastEntryUtc { get; set; }
    public string? ArchiveKey { get; set; }
    public string? ArchiveSha256 { get; set; }
    public long RowsArchived { get; set; }
    public long RowsDeleted { get; set; }
    public int FilesDeleted { get; set; }
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Which service answered with a <see cref="CompanyDataPurged"/>, carried in the <see cref="Header"/>
/// message header (not a field, so services built against an older copy of this library still
/// interoperate). hrms-api's answers carry no header and count as <see cref="Hrms"/>. An operator's
/// erasure finishes only once every service holding company data has confirmed.
/// </summary>
public static class PurgeSources
{
    public const string Header = "X-Purge-Source";
    public const string Hrms = "hrms";
    public const string Adms = "adms";
    public const string Portal = "portal";

    /// <summary>The source an answer came from: the header's value, or hrms-api when there is none.</summary>
    public static string From(string? header) =>
        string.IsNullOrWhiteSpace(header) ? Hrms : header.Trim().ToLowerInvariant();
}
