using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>
/// Who goes with erased companies: everyone whose only memberships are in them (plus, for a whole
/// account, its owner). Anyone who still belongs to (or owns) a company outside them keeps their login
/// and is only detached -- an account's owner, when just one of its companies goes. Removed memberships
/// elsewhere don't keep a login alive.
/// </summary>
public static class ErasureUsers
{
    public static async Task<(List<Guid> Delete, List<Guid> Detach)> PartitionAsync(
        TenantContext context, IReadOnlyCollection<Guid> tenantIds, Guid? alsoInclude, CancellationToken token)
    {
        var erased = tenantIds.ToList();
        var candidates = await context.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => erased.Contains(m.TenantId) && m.UserId != Guid.Empty)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(token);
        if (alsoInclude is { } extra && !candidates.Contains(extra)) candidates.Add(extra);

        var memberElsewhere = await context.Memberships.AsNoTracking()
            .Where(m => candidates.Contains(m.UserId) && !erased.Contains(m.TenantId))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(token);
        var ownerElsewhere = await context.Tenants.AsNoTracking()
            .Where(t => candidates.Contains(t.UserId) && !erased.Contains(t.Id))
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync(token);

        var keep = memberElsewhere.Concat(ownerElsewhere).ToHashSet();
        return (candidates.Where(id => !keep.Contains(id)).ToList(), candidates.Where(keep.Contains).ToList());
    }
}
