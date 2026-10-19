using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Core.Utilities;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>Emails and names of logins, which Auth Service holds. A seam so the preview can be tested without it.</summary>
public interface IUserLookup
{
    Task<IReadOnlyDictionary<Guid, (string? Email, string? Name)>> FindAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token);
}

/// <summary>Asks Auth Service over gRPC; returns nothing (not an error) when it can't be reached.</summary>
public sealed class GrpcUserLookup(UserInfoService users) : IUserLookup
{
    public async Task<IReadOnlyDictionary<Guid, (string? Email, string? Name)>> FindAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, (string?, string?)>();
        var found = await users.GetUsersByIdsAsync(userIds.Select(id => id.ToString()), token);
        return found
            .Where(u => Guid.TryParse(u.Id, out _))
            .GroupBy(u => Guid.Parse(u.Id))
            .ToDictionary(g => g.Key, g => ((string?)g.First().Email, (string?)g.First().FullName));
    }
}

/// <summary>A login an erasure keeps, because its person also belongs to (or owns) a company it doesn't erase.</summary>
public sealed record KeptLogin(Guid UserId, string? Email, string? Name, IReadOnlyList<string> OtherCompanies);

/// <summary>What approving a deletion request will do to logins.</summary>
public sealed record DeletionImpact(int LoginsDeleted, IReadOnlyList<KeptLogin> Kept);

/// <summary>
/// Previews, for the approving operator, whose logins a deletion removes and whose it keeps (and why).
/// Uses the same rule the erasure applies (<see cref="ErasureUsers"/>), so the preview and the outcome agree.
/// </summary>
public class DeletionImpactService
{
    private readonly IUnitOfWorkService _uow;
    private readonly IUserLookup _users;

    public DeletionImpactService(IUnitOfWorkService uow, IUserLookup users)
    {
        _uow = uow;
        _users = users;
    }

    private TenantContext Context => _uow.Context;

    public async Task<DeletionImpact> GetAsync(Guid requestId, CancellationToken token = default)
    {
        var request = await Context.AccountDeletionRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, token)
            ?? throw new KeyNotFoundException("Deletion request not found.");

        List<Guid> erased = request.TenantId is { } tenantId
            ? [tenantId]
            : await Context.Tenants.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.UserId == request.OwnerUserId).Select(t => t.Id).ToListAsync(token);
        var (delete, keep) = await ErasureUsers.PartitionAsync(
            Context, erased, request.TenantId == null ? request.OwnerUserId : null, token);
        if (keep.Count == 0) return new DeletionImpact(delete.Count, []);

        var memberOf = await (from m in Context.Memberships.AsNoTracking()
                              join t in Context.Tenants.AsNoTracking() on m.TenantId equals t.Id
                              where keep.Contains(m.UserId) && !erased.Contains(m.TenantId)
                              select new { m.UserId, t.TenantName }).ToListAsync(token);
        var owns = await Context.Tenants.AsNoTracking()
            .Where(t => keep.Contains(t.UserId) && !erased.Contains(t.Id))
            .Select(t => new { t.UserId, t.TenantName })
            .ToListAsync(token);
        // Fallback when Auth Service can't be asked: what the erased companies' memberships recorded.
        var recorded = await Context.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => keep.Contains(m.UserId))
            .Select(m => new { m.UserId, m.InvitedEmail, m.FullName })
            .ToListAsync(token);
        var looked = await _users.FindAsync(keep, token);

        var kept = keep.Select(userId =>
        {
            looked.TryGetValue(userId, out var identity);
            var mine = recorded.Where(r => r.UserId == userId).ToList();
            return new KeptLogin(
                userId,
                NonEmpty(identity.Email) ?? mine.Select(r => r.InvitedEmail).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e)),
                NonEmpty(identity.Name) ?? mine.Select(r => r.FullName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                memberOf.Where(x => x.UserId == userId).Select(x => x.TenantName)
                    .Concat(owns.Where(x => x.UserId == userId).Select(x => x.TenantName))
                    .Select(name => name ?? "(unnamed company)")
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList());
        }).OrderBy(k => k.Email ?? k.Name).ToList();
        return new DeletionImpact(delete.Count, kept);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
