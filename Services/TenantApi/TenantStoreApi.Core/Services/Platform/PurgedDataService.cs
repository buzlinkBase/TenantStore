using Microsoft.EntityFrameworkCore;
using Refit;
using Serilog;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

// What hrms-api's internal purged-data endpoints answer (see hrms-api InternalPurgedDataController).
public sealed record PurgedAuditChange(string Property, string? OldValue, string? NewValue);

public sealed record PurgedAuditEntry(
    Guid Id, string Action, string Module, string? EntityType, string? ResourceId, string? UserId, string? UserName,
    string? Description, string? IpAddress, string? UserAgent, string Timestamp, string Status, string? Source,
    List<PurgedAuditChange> Changes);

public sealed record PurgedAuditPage(List<PurgedAuditEntry> Items, int Total, int Page, int PageSize);

public sealed record PurgedArchiveLink(string? Url, string FileName, int ExpiresInMinutes);

/// <summary>What the console may filter a purged company's audit trail by.</summary>
public sealed record PurgedAuditQuery(string? Search = null, string? Action = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 25);

/// <summary>hrms-api's internal purged-data endpoints (Api-key protected), one company per call.</summary>
public interface IPurgedDataApi
{
    [Get("/api/v1/internal/purged/{tenantId}/audit")]
    Task<ResponseModel<PurgedAuditPage>> GetAuditAsync(Guid tenantId, [Query] PurgedAuditQuery query, CancellationToken token);

    [Get("/api/v1/internal/purged/{tenantId}/archive-link")]
    Task<ResponseModel<PurgedArchiveLink>> GetArchiveLinkAsync(Guid tenantId, string archiveKey, CancellationToken token);
}

public sealed record PurgedDataQuery(
    string? Search = null,
    /// <summary>"purged", "destroyed" or "hold"; null for all.</summary>
    string? Filter = null,
    int Page = 1,
    int PageSize = 25)
{
    public PageRequest Paging => new(Page, PageSize);
}

/// <summary>One purged company, with its account's retention dates.</summary>
public sealed record PurgedCompanyView(
    Guid TenantId,
    string CompanyName,
    bool HasArchive,
    long RowsArchived,
    string? ArchiveSha256,
    DateTime PurgedAtUtc,
    DateTime? DestroyedAtUtc);

/// <summary>An account whose data was purged, with the companies it had.</summary>
public sealed record PurgedAccountView(
    Guid OwnerUserId,
    string? OwnerEmail,
    string? OwnerName,
    DataPurgeState PurgeState,
    DateTime? DataPurgedAtUtc,
    DateTime? ArchiveRetainUntilUtc,
    bool LegalHold,
    string? LegalHoldReason,
    IReadOnlyList<PurgedCompanyView> Companies);

public sealed record PurgedAccountPage(IReadOnlyList<PurgedAccountView> Items, int Total, int Page, int PageSize);

/// <summary>
/// The Platform Console's view of what a purge kept. Finding purged accounts needs no ids (it searches the
/// owner's email and the company names). Reading a company's audit trail or downloading its archive
/// releases personal data, so each needs a reason and is recorded in the account's billing history -- the
/// platform audit trail -- as the operator who did it. Terms 7.8: an archive is released only at the
/// Customer's written request or where the law requires; the reason is where that is written down.
/// </summary>
public class PurgedDataService
{
    private readonly IUnitOfWorkService _uow;
    private readonly PlatformDirectoryService _directory;
    private readonly BillingEventService _events;
    private readonly IPurgedDataApi? _api;

    public PurgedDataService(IUnitOfWorkService uow, PlatformDirectoryService directory, BillingEventService events, IPurgedDataApi? api = null)
    {
        _uow = uow;
        _directory = directory;
        _events = events;
        _api = api;
    }

    private TenantContext Context => _uow.Context;

    private IPurgedDataApi Api =>
        _api ?? throw new SubscriptionRuleException("Purged data isn't reachable from here (Subscriptions:Usage:HrmsApiUrl isn't set).");

    public async Task<PurgedAccountPage> ListAsync(PurgedDataQuery query, CancellationToken token)
    {
        var accounts = Context.AccountSubscriptions.AsNoTracking()
            .Where(s => s.PurgeState == DataPurgeState.Purged || s.PurgeState == DataPurgeState.ArchiveDestroyed);

        accounts = query.Filter switch
        {
            "purged" => accounts.Where(s => s.PurgeState == DataPurgeState.Purged),
            "destroyed" => accounts.Where(s => s.PurgeState == DataPurgeState.ArchiveDestroyed),
            "hold" => accounts.Where(s => s.LegalHold),
            _ => accounts,
        };

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var byOwner = await Context.Memberships.AsNoTracking()
                .Where(m => m.IsHidden && m.InvitedEmail != null && m.InvitedEmail.Contains(search))
                .Select(m => m.UserId).ToListAsync(token);
            var byCompany = await Context.PurgedCompanyArchives.AsNoTracking()
                .Where(a => a.CompanyName.Contains(search)).Select(a => a.OwnerUserId).ToListAsync(token);
            var matches = byOwner.Concat(byCompany).Distinct().ToList();
            accounts = accounts.Where(s => matches.Contains(s.OwnerUserId));
        }

        var total = await accounts.CountAsync(token);
        var page = await accounts.OrderByDescending(s => s.DataPurgedAtUtc)
            .Skip(query.Paging.Skip).Take(query.Paging.SafePageSize).ToListAsync(token);

        var ownerIds = page.Select(s => s.OwnerUserId).ToList();
        var identities = await _directory.OwnerIdentitiesAsync(ownerIds, token);
        var archives = (await Context.PurgedCompanyArchives.AsNoTracking()
                .Where(a => ownerIds.Contains(a.OwnerUserId)).OrderBy(a => a.CompanyName).ToListAsync(token))
            .GroupBy(a => a.OwnerUserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PurgedCompanyView>)g.Select(ToView).ToList());

        var items = page.Select(s =>
        {
            identities.TryGetValue(s.OwnerUserId, out var identity);
            return new PurgedAccountView(
                s.OwnerUserId, identity.Email, identity.Name, s.PurgeState, s.DataPurgedAtUtc, s.ArchiveRetainUntilUtc,
                s.LegalHold, s.LegalHoldReason, archives.GetValueOrDefault(s.OwnerUserId) ?? []);
        }).ToList();
        return new PurgedAccountPage(items, total, query.Paging.SafePage, query.Paging.SafePageSize);
    }

    /// <summary>The company's retained audit trail, read from hrms-api. A reason is required and recorded.</summary>
    public async Task<PurgedAuditPage> GetAuditAsync(
        Guid ownerUserId, Guid tenantId, PurgedAuditQuery query, string? reason, PlatformOperator actor, CancellationToken token)
    {
        reason = RequireReason(reason);
        var archive = await RequirePurgedCompanyAsync(ownerUserId, tenantId, token);
        var page = await CallAsync(() => Api.GetAuditAsync(tenantId, query, token));
        // Only the first page is a fresh "look": paging through the same trail isn't recorded again.
        if (query.Page <= 1)
        {
            await RecordAsync(archive, BillingEventTypes.PurgedAuditViewed, new { search = query.Search, query.Action }, reason, actor, token);
            await _uow.CommitChangesAsync("", token);
        }
        return page;
    }

    /// <summary>A short-lived link to the company's archive. A reason is required and recorded before the link is handed out.</summary>
    public async Task<PurgedArchiveLink> GetArchiveLinkAsync(
        Guid ownerUserId, Guid tenantId, string? reason, PlatformOperator actor, CancellationToken token)
    {
        reason = RequireReason(reason);
        var archive = await RequirePurgedCompanyAsync(ownerUserId, tenantId, token);
        if (archive.DestroyedAtUtc != null) throw new SubscriptionRuleException("This company's archive has been destroyed.");
        if (string.IsNullOrEmpty(archive.ArchiveKey)) throw new SubscriptionRuleException("This company had nothing to archive.");

        // Recorded first: a link that was handed out must never be missing from the trail.
        await RecordAsync(archive, BillingEventTypes.PurgedArchiveDownloaded, new { archive.ArchiveKey, archive.ArchiveSha256 }, reason, actor, token);
        await _uow.CommitChangesAsync("", token);
        return await CallAsync(() => Api.GetArchiveLinkAsync(tenantId, archive.ArchiveKey!, token));
    }

    private async Task<PurgedCompanyArchive> RequirePurgedCompanyAsync(Guid ownerUserId, Guid tenantId, CancellationToken token) =>
        await Context.PurgedCompanyArchives.AsNoTracking()
            .FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId && a.TenantId == tenantId, token)
        ?? throw new KeyNotFoundException("This company wasn't purged, or its record is gone.");

    private async Task RecordAsync(
        PurgedCompanyArchive archive, string type, object details, string reason, PlatformOperator actor, CancellationToken token)
    {
        var subscription = await Context.AccountSubscriptions.FirstOrDefaultAsync(s => s.Id == archive.SubscriptionId, token);
        await _events.AddAsync(subscription, type, new { tenantId = archive.TenantId, company = archive.CompanyName, details, reason },
            actor.Actor, ownerUserId: archive.OwnerUserId, token: token);
    }

    private static PurgedCompanyView ToView(PurgedCompanyArchive a) => new(
        a.TenantId, a.CompanyName, !string.IsNullOrEmpty(a.ArchiveKey) && a.DestroyedAtUtc == null, a.RowsArchived,
        a.ArchiveSha256, a.PurgedAtUtc, a.DestroyedAtUtc);

    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 3)
            throw new SubscriptionRuleException("Give a reason (at least 3 characters): who asked, or what legal duty it serves.");
        return trimmed.Length > 500 ? throw new SubscriptionRuleException("The reason is too long (max 500 characters).") : trimmed;
    }

    private static async Task<T> CallAsync<T>(Func<Task<ResponseModel<T>>> call)
    {
        try
        {
            var response = await call();
            return response.Data ?? throw new SubscriptionRuleException("Purged data returned no answer.");
        }
        catch (ApiException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            throw new SubscriptionRuleException(ex.StatusCode == System.Net.HttpStatusCode.BadRequest
                ? "hrms-api refused the request (the archive may no longer exist)."
                : "hrms-api refused the request.");
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            Log.Logger.Warning(ex, "Purged-data call to hrms-api failed");
            throw new SubscriptionRuleException("Purged data isn't reachable right now. Try again in a moment.");
        }
    }
}
