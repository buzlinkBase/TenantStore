using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Legal;

/// <summary>Where a company stands with the Terms.</summary>
public sealed record TermsStatus(
    int? CurrentVersion,
    DateTime? CurrentEffectiveAtUtc,
    string? ChangeSummary,
    int? AcceptedVersion,
    DateTime? AcceptedAtUtc,
    string? AcceptedBy,
    /// <summary>True when a version the company hasn't accepted asks every company to accept it (never while not live).</summary>
    bool RequiresAcceptance,
    /// <summary>Whether the Terms are in effect at all (the console's "Terms of Service live" switch).</summary>
    bool IsLive);

/// <summary>Who accepts, and from where -- the evidence kept with each acceptance.</summary>
public sealed record TermsAcceptor(Guid UserId, string? Email, string? Name, string? IpAddress, string? UserAgent);

public sealed record TermsAcceptanceView(
    Guid Id,
    Guid TenantId,
    string? CompanyName,
    Guid UserId,
    string? UserEmail,
    string? UserName,
    int Version,
    DateTime AcceptedAtUtc,
    string? IpAddress);

public sealed record TermsAcceptanceQuery
{
    public int? Version { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed record TermsAcceptancePage(List<TermsAcceptanceView> Items, int Total, int Page, int PageSize);

/// <summary>
/// Companies accepting the Terms. A company accepts through an owner or admin (checked by the
/// caller); each acceptance is kept as evidence and never edited. Doesn't commit.
/// </summary>
public class TermsAcceptanceService
{
    private const string DocType = LegalDocumentTypes.Terms;

    private readonly IUnitOfWorkService _uow;
    private readonly IOptions<SubscriptionOptions> _options;
    private readonly TimeProvider _clock;

    public TermsAcceptanceService(IUnitOfWorkService uow, IOptions<SubscriptionOptions> options, TimeProvider clock)
    {
        _uow = uow;
        _options = options;
        _clock = clock;
    }

    private bool IsLive => _options.Value.TermsLive;

    private TenantContext Context => _uow.Context;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    private IQueryable<LegalDocument> Published =>
        Context.LegalDocuments.AsNoTracking()
            .Where(d => d.DocType == DocType && d.LegalStatus != LegalDocumentStatuses.Draft && d.Version != null);

    public async Task<TermsStatus> GetStatusAsync(Guid tenantId, CancellationToken token = default)
    {
        var current = await Published.OrderByDescending(d => d.Version).FirstOrDefaultAsync(token);
        var accepted = await Context.TermsAcceptances.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.Version).ThenByDescending(a => a.AcceptedAtUtc)
            .FirstOrDefaultAsync(token);

        var acceptedVersion = accepted?.Version ?? 0;
        // A wording fix (RequiresReacceptance off) doesn't ask again; any newer version that does, does.
        var requires = IsLive && current != null && await Published.AnyAsync(d => d.Version > acceptedVersion && d.RequiresReacceptance, token);

        return new TermsStatus(
            current?.Version, current?.EffectiveAtUtc, current?.ChangeSummary,
            accepted?.Version, accepted?.AcceptedAtUtc, accepted?.UserName ?? accepted?.UserEmail, requires, IsLive);
    }

    /// <summary>Records the company accepting <paramref name="version"/>, which must be the current one. Accepting it again changes nothing.</summary>
    public async Task AcceptAsync(Guid tenantId, int version, TermsAcceptor acceptor, CancellationToken token = default)
    {
        if (!IsLive) throw new SubscriptionRuleException("The Terms of Service aren't in effect yet.");
        var current = await Published.OrderByDescending(d => d.Version).FirstOrDefaultAsync(token)
            ?? throw new SubscriptionRuleException("There are no Terms of Service to accept yet.");
        if (current.Version != version)
            throw new SubscriptionRuleException("These Terms have been updated. Reload the page to read and accept the latest version.");

        if (await Context.TermsAcceptances.AnyAsync(a => a.TenantId == tenantId && a.Version == version, token)) return;

        await Context.TermsAcceptances.AddAsync(new TermsAcceptance
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = acceptor.UserId,
            UserEmail = Clip(acceptor.Email, 256),
            UserName = Clip(acceptor.Name, 256),
            Version = version,
            AcceptedAtUtc = _clock.GetUtcNow().UtcDateTime,
            IpAddress = Clip(acceptor.IpAddress, 64),
            UserAgent = Clip(acceptor.UserAgent, 512),
        }, token);
    }

    /// <summary>For the console: who accepted, newest first.</summary>
    public async Task<TermsAcceptancePage> ListAsync(TermsAcceptanceQuery query, CancellationToken token = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);

        var rows = Context.TermsAcceptances.AsNoTracking().AsQueryable();
        if (query.Version is { } version) rows = rows.Where(a => a.Version == version);

        var joined = rows.GroupJoin(Context.Tenants.AsNoTracking().IgnoreQueryFilters(),
                a => a.TenantId, t => t.Id, (a, tenants) => new { a, tenants })
            .SelectMany(x => x.tenants.DefaultIfEmpty(), (x, t) => new { x.a, CompanyName = t != null ? t.TenantName : null });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            joined = joined.Where(x =>
                (x.CompanyName != null && x.CompanyName.Contains(term)) ||
                (x.a.UserEmail != null && x.a.UserEmail.Contains(term)) ||
                (x.a.UserName != null && x.a.UserName.Contains(term)));
        }

        var total = await joined.CountAsync(token);
        var items = await joined
            .OrderByDescending(x => x.a.AcceptedAtUtc)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new TermsAcceptanceView(x.a.Id, x.a.TenantId, x.CompanyName, x.a.UserId, x.a.UserEmail,
                x.a.UserName, x.a.Version, x.a.AcceptedAtUtc, x.a.IpAddress))
            .ToListAsync(token);
        return new TermsAcceptancePage(items, total, page, size);
    }

    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value.Trim() : value.Trim()[..max];
}
