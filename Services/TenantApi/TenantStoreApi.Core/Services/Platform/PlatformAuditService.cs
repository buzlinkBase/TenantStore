using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

public sealed record AuditQuery(
    Guid? OwnerUserId = null,
    /// <summary>Contains-match on who did it ("platform:ana@onepunch.com", "system" for the lifecycle job).</summary>
    string? Actor = null,
    /// <summary>An exact event type, or a prefix ending in '.' / '*' (e.g. "payment." or "plan.*").</summary>
    string? Type = null,
    /// <summary>True: only actions by platform operators. False: only system/owner events. Null: everything.</summary>
    bool? OperatorsOnly = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Page = 1,
    int PageSize = 25)
{
    public PageRequest Paging => new(Page, PageSize);
}

public sealed record AuditEntry(
    Guid Id,
    DateTime OccurredAtUtc,
    string Type,
    string? Actor,
    Guid? OwnerUserId,
    string? OwnerEmail,
    string? PayloadJson);

/// <summary>
/// The platform audit trail: the billing history across every account, which already records each
/// operator action (who, when, what, and the reason they gave) as well as what the system did
/// (renewals, lapses, reminders). Read-only and append-only -- events are never edited or deleted.
/// </summary>
public class PlatformAuditService
{
    public const string OperatorPrefix = "platform";

    private readonly IUnitOfWorkService _uow;
    private readonly PlatformDirectoryService _directory;

    public PlatformAuditService(IUnitOfWorkService uow, PlatformDirectoryService directory)
    {
        _uow = uow;
        _directory = directory;
    }

    private TenantContext Context => _uow.Context;

    public async Task<PagedResult<AuditEntry>> ListAsync(AuditQuery query, CancellationToken token = default)
    {
        var events = Context.BillingEvents.AsNoTracking().AsQueryable();

        if (query.OwnerUserId is { } owner) events = events.Where(e => e.OwnerUserId == owner);
        if (query.FromUtc is { } from) events = events.Where(e => e.OccurredAtUtc >= from);
        if (query.ToUtc is { } to) events = events.Where(e => e.OccurredAtUtc <= to);

        var actor = query.Actor?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(actor)) events = events.Where(e => e.Actor != null && e.Actor.ToLower().Contains(actor));

        if (query.OperatorsOnly is { } operatorsOnly)
        {
            events = operatorsOnly
                ? events.Where(e => e.Actor != null && e.Actor.StartsWith(OperatorPrefix))
                : events.Where(e => e.Actor == null || !e.Actor.StartsWith(OperatorPrefix));
        }

        var type = query.Type?.Trim();
        if (!string.IsNullOrEmpty(type))
        {
            if (type.EndsWith('*') || type.EndsWith('.'))
            {
                var prefix = type.TrimEnd('*');
                events = events.Where(e => e.Type.StartsWith(prefix));
            }
            else
            {
                events = events.Where(e => e.Type == type);
            }
        }

        var total = await events.CountAsync(token);
        var paging = query.Paging;
        var page = await events
            .OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Skip(paging.Skip).Take(paging.SafePageSize)
            .ToListAsync(token);

        var ownerIds = page.Where(e => e.OwnerUserId.HasValue).Select(e => e.OwnerUserId!.Value).Distinct().ToList();
        var identities = await _directory.OwnerIdentitiesAsync(ownerIds, token);

        var items = page.Select(e =>
        {
            (string? Email, string? Name) identity = default;
            if (e.OwnerUserId is { } id) identities.TryGetValue(id, out identity);
            return new AuditEntry(e.Id, e.OccurredAtUtc, e.Type, e.Actor, e.OwnerUserId, identity.Email, e.PayloadJson);
        }).ToList();
        return new PagedResult<AuditEntry>(items, total, paging.SafePage, paging.SafePageSize);
    }

    /// <summary>Every event type seen so far, for the console's filter.</summary>
    public Task<List<string>> ListTypesAsync(CancellationToken token = default) =>
        Context.BillingEvents.AsNoTracking().Select(e => e.Type).Distinct().OrderBy(t => t).ToListAsync(token);
}
