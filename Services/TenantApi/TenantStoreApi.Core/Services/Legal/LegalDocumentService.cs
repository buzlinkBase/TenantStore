using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Legal;

public sealed record LegalDocumentView(
    Guid? Id,
    string DocType,
    string Status,
    int? Version,
    string BodyMarkdown,
    string? RenderedMarkdown,
    string? ChangeSummary,
    bool RequiresReacceptance,
    DateTime? EffectiveAtUtc,
    DateTime? PublishedAtUtc,
    string? PublishedBy,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    /// <summary>False for a draft not saved yet (prefilled from the latest version or the template).</summary>
    bool IsSaved);

/// <summary>What the public sees of a published version.</summary>
public sealed record PublishedLegalDocument(
    int Version,
    string RenderedMarkdown,
    string? ChangeSummary,
    bool RequiresReacceptance,
    DateTime? EffectiveAtUtc,
    DateTime? PublishedAtUtc);

public sealed record LegalPreview(string RenderedMarkdown, IReadOnlyList<string> UnknownTokens);

public sealed record SaveLegalDraft
{
    public string? BodyMarkdown { get; set; }
    public string? ChangeSummary { get; set; }
    public bool RequiresReacceptance { get; set; } = true;
    public DateTime? EffectiveAtUtc { get; set; }
}

public sealed record LegalTokenView(string Name, string Description, string CurrentValue);

/// <summary>
/// The Terms of Service as the console writes them: one draft at a time, previewed with the live
/// settings, then published as the next numbered version with its text frozen. Published versions
/// are never edited. Doesn't commit: the caller owns the unit of work.
/// </summary>
public class LegalDocumentService
{
    public const int MaxBodyLength = 200_000;

    private readonly IUnitOfWorkService _uow;
    private readonly BillingEventService _events;
    private readonly IOptions<SubscriptionOptions> _options;
    private readonly TimeProvider _clock;

    public LegalDocumentService(IUnitOfWorkService uow, BillingEventService events, IOptions<SubscriptionOptions> options, TimeProvider clock)
    {
        _uow = uow;
        _events = events;
        _options = options;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    private IQueryable<LegalDocument> DocumentsOf(string docType) => Context.LegalDocuments.Where(d => d.DocType == docType);

    private Task<LegalDocument?> FindDraftAsync(string docType, CancellationToken token) =>
        DocumentsOf(docType).FirstOrDefaultAsync(d => d.LegalStatus == LegalDocumentStatuses.Draft, token);

    private Task<LegalDocument?> FindCurrentAsync(string docType, CancellationToken token) =>
        DocumentsOf(docType)
            .Where(d => d.LegalStatus == LegalDocumentStatuses.Published)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(token);

    public IReadOnlyList<LegalTokenView> Tokens() =>
        LegalTokenCatalog.All.Select(t => new LegalTokenView(t.Name, t.Description, t.Resolve(_options.Value))).ToList();

    /// <summary>The draft, or -- when there is none -- an unsaved one prefilled from the current version (or the template).</summary>
    public async Task<LegalDocumentView> GetDraftAsync(string docType, CancellationToken token = default)
    {
        var draft = await FindDraftAsync(docType, token);
        if (draft != null) return ToView(draft, saved: true);

        var current = await FindCurrentAsync(docType, token);
        return new LegalDocumentView(
            null, docType, LegalDocumentStatuses.Draft, null,
            current?.BodyMarkdown ?? TermsOfServiceTemplate.Markdown,
            null, null, true, null, null, null, null, null, IsSaved: false);
    }

    public async Task<LegalDocumentView> SaveDraftAsync(string docType, SaveLegalDraft payload, string? actor, CancellationToken token = default)
    {
        var body = payload.BodyMarkdown?.Trim() ?? string.Empty;
        if (body.Length == 0) throw new SubscriptionRuleException("The document can't be empty.");
        if (body.Length > MaxBodyLength) throw new SubscriptionRuleException($"The document can be at most {MaxBodyLength:N0} characters.");
        var summary = payload.ChangeSummary?.Trim();
        if (summary?.Length > 2000) throw new SubscriptionRuleException("The change summary can be at most 2,000 characters.");

        var draft = await FindDraftAsync(docType, token);
        if (draft == null)
        {
            draft = new LegalDocument
            {
                Id = Guid.CreateVersion7(),
                DocType = docType,
                LegalStatus = LegalDocumentStatuses.Draft,
                CreatedBy = actor,
            };
            await Context.LegalDocuments.AddAsync(draft, token);
        }

        draft.BodyMarkdown = body;
        draft.ChangeSummary = string.IsNullOrEmpty(summary) ? null : summary;
        draft.RequiresReacceptance = payload.RequiresReacceptance;
        draft.EffectiveAtUtc = payload.EffectiveAtUtc;
        draft.UpdatedBy = actor;
        return ToView(draft, saved: true);
    }

    public LegalPreview Preview(string? markdown)
    {
        var body = markdown ?? string.Empty;
        var unknown = LegalTokenRenderer.UnknownTokens(body);
        return new LegalPreview(unknown.Count > 0 ? body : LegalTokenRenderer.Render(body, _options.Value), unknown);
    }

    public async Task DiscardDraftAsync(string docType, CancellationToken token = default)
    {
        var draft = await FindDraftAsync(docType, token);
        if (draft != null) Context.LegalDocuments.Remove(draft);
    }

    /// <summary>Publishes the saved draft as the next version; the one before it is superseded.</summary>
    public async Task<LegalDocumentView> PublishAsync(string docType, string? actor, CancellationToken token = default)
    {
        var draft = await FindDraftAsync(docType, token)
            ?? throw new SubscriptionRuleException("There is no saved draft to publish.");
        var rendered = LegalTokenRenderer.Render(draft.BodyMarkdown, _options.Value);

        var current = await FindCurrentAsync(docType, token);
        if (current == null && !draft.RequiresReacceptance)
            throw new SubscriptionRuleException("The first version must be accepted by every company.");

        var lastVersion = await DocumentsOf(docType).MaxAsync(d => d.Version, token) ?? 0;
        if (current != null) current.LegalStatus = LegalDocumentStatuses.Superseded;

        draft.LegalStatus = LegalDocumentStatuses.Published;
        draft.Version = lastVersion + 1;
        draft.RenderedMarkdown = rendered;
        draft.PublishedAtUtc = Now;
        draft.EffectiveAtUtc ??= draft.PublishedAtUtc;
        draft.PublishedBy = actor;

        await _events.AddAsync(null, "legal.published",
            new { docType, version = draft.Version, draft.RequiresReacceptance, draft.ChangeSummary }, actor, token: token);
        return ToView(draft, saved: true);
    }

    /// <summary>Every published version, newest first, plus the draft when there is one.</summary>
    public async Task<List<LegalDocumentView>> ListAsync(string docType, CancellationToken token = default)
    {
        var rows = await DocumentsOf(docType).AsNoTracking()
            .OrderByDescending(d => d.Version == null)
            .ThenByDescending(d => d.Version)
            .ToListAsync(token);
        return rows.Select(d => ToView(d, saved: true)).ToList();
    }

    /// <summary>Whether the Terms are in effect (the console's switch). The public endpoints show nothing while they aren't.</summary>
    public bool IsLive => _options.Value.TermsLive;

    public async Task<PublishedLegalDocument?> GetPublishedAsync(string docType, int? version, CancellationToken token = default)
    {
        var row = version is { } v
            ? await DocumentsOf(docType).AsNoTracking()
                .FirstOrDefaultAsync(d => d.Version == v && d.LegalStatus != LegalDocumentStatuses.Draft, token)
            : await FindCurrentAsync(docType, token);
        return row == null
            ? null
            : new PublishedLegalDocument(row.Version!.Value, row.RenderedMarkdown ?? string.Empty, row.ChangeSummary,
                row.RequiresReacceptance, row.EffectiveAtUtc, row.PublishedAtUtc);
    }

    private static LegalDocumentView ToView(LegalDocument d, bool saved) =>
        new(d.Id, d.DocType, d.LegalStatus, d.Version, d.BodyMarkdown, d.RenderedMarkdown, d.ChangeSummary,
            d.RequiresReacceptance, d.EffectiveAtUtc, d.PublishedAtUtc, d.PublishedBy,
            d.UpdatedAt ?? d.CreatedAt, d.UpdatedBy, saved);
}
