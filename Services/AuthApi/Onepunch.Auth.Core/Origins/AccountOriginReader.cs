using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Infrastructure;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Origins;

/// <summary>One account's origin and the early steps it reached. No personal details.</summary>
public sealed record AccountOriginInfo(
    Guid UserId,
    string Origin,
    DateTime? CreatedAtUtc,
    DateTime? EmailConfirmedAtUtc,
    DateTime? FirstHrisSignInAtUtc,
    DateTime? FirstPortalSignInAtUtc,
    DateTime? OwnCompanyRequestedAtUtc);

public sealed record AccountOriginPage(IReadOnlyList<AccountOriginInfo> Accounts, int UndatedCount, bool Truncated);

/// <summary>
/// Reads how accounts began, for Tenant Service's Acquisition report: specific accounts, or everyone who signed up in a
/// date range. Soft-deleted accounts are included: a person who later left still counts in the history.
/// </summary>
public sealed class AccountOriginReader(AuthContext context)
{
    public const int MaxRows = 5000;

    public async Task<AccountOriginPage> ReadAsync(IReadOnlyCollection<Guid> userIds, DateTime? createdFromUtc, DateTime? createdToUtc, CancellationToken ct)
    {
        var users = context.Users.IgnoreQueryFilters().AsNoTracking();
        var byIds = userIds.Count > 0;
        if (byIds)
        {
            var ids = userIds.Take(MaxRows).ToList();
            users = users.Where(u => ids.Contains(u.Id));
        }
        else
        {
            // Without ids a range is required: an open call would dump every account.
            if (createdFromUtc is null && createdToUtc is null) return new AccountOriginPage([], 0, false);
            users = users.Where(u => u.CreatedAtUtc != null);
            if (createdFromUtc is { } from) users = users.Where(u => u.CreatedAtUtc >= from);
            if (createdToUtc is { } to) users = users.Where(u => u.CreatedAtUtc < to);
        }

        var rows = await users.OrderBy(u => u.CreatedAtUtc).Take(MaxRows + 1)
            .Select(u => new { u.Id, u.Origin, u.CreatedAtUtc, u.EmailConfirmedAtUtc, u.FirstHrisSignInAtUtc, u.FirstPortalSignInAtUtc })
            .ToListAsync(ct);
        var truncated = rows.Count > MaxRows;
        if (truncated) rows.RemoveAt(rows.Count - 1);

        var rowIds = rows.Select(r => r.Id).ToList();
        var ownCompany = (await context.TenantCreationRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(r => rowIds.Contains(r.UserId))
                .GroupBy(r => r.UserId)
                .Select(g => new { UserId = g.Key, At = g.Min(r => r.CreatedAt) })
                .ToListAsync(ct))
            .ToDictionary(x => x.UserId, x => x.At);

        var accounts = rows.Select(r => new AccountOriginInfo(
            r.Id,
            AccountOrigins.IsKnown(r.Origin) ? r.Origin : AccountOrigins.Unknown,
            Utc(r.CreatedAtUtc), Utc(r.EmailConfirmedAtUtc), Utc(r.FirstHrisSignInAtUtc), Utc(r.FirstPortalSignInAtUtc),
            ownCompany.TryGetValue(r.Id, out var at) ? Utc(at) : null)).ToList();

        var undated = byIds ? 0 : await context.Users.IgnoreQueryFilters().CountAsync(u => u.CreatedAtUtc == null, ct);
        return new AccountOriginPage(accounts, undated, truncated);
    }

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
