using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;

namespace Onepunch.Auth.Core.Activity;

/// <summary>
/// When each company last had an HRIS sign-in. Every login and every session refresh writes a new
/// <see cref="RefreshToken"/> carrying the session's company, so the newest one is the best "last active" signal.
/// Client Portal sessions are left out: they say nothing about the company's own staff.
/// </summary>
public sealed class TenantActivityReader(AuthContext context)
{
    public const int MaxTenants = 1000;

    public async Task<IReadOnlyDictionary<Guid, DateTime>> ReadLastActiveAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken ct)
    {
        var ids = tenantIds.Distinct().Take(MaxTenants).Select(id => (Guid?)id).ToList();
        if (ids.Count == 0) return new Dictionary<Guid, DateTime>();

        var rows = await context.RefreshTokens.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Audience == null && ids.Contains(t.TenantId))
            .GroupBy(t => t.TenantId)
            .Select(g => new { TenantId = g.Key, At = g.Max(t => t.CreatedAt) })
            .ToListAsync(ct);

        return rows
            .Where(r => r.TenantId.HasValue)
            .ToDictionary(r => r.TenantId!.Value, r => DateTime.SpecifyKind(r.At, DateTimeKind.Utc));
    }
}
