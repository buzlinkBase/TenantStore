using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib.Cache;
using OnePunch.Auth.Core;
using Serilog;

namespace Onepunch.Auth.Core.Services;

/// <summary>
/// Replaces the old 30-min HTTP-backed AccountTenantsProvider cache with a 5-min gRPC-backed
/// membership snapshot (Flows E/H/I). Falls back to the user's own denormalized DefaultTenant*
/// fields if Tenant Service is unreachable/slow, rather than failing login outright.
/// </summary>
public class MembershipCacheService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly ICacheService _cacheService;
    private readonly IUnitOfWorkService _uow;
    private readonly MembershipGrpcClient _membershipGrpcClient;

    public MembershipCacheService(
        ICacheService cacheService,
        IUnitOfWorkService uow,
        MembershipGrpcClient membershipGrpcClient)
    {
        _cacheService = cacheService;
        _uow = uow;
        _membershipGrpcClient = membershipGrpcClient;
    }

    private static string CacheKey(Guid userId) => $"membership:{userId}";

    /// <summary>
    /// deadlineMilliseconds defaults to 500ms (more generous than the Gateway's 200ms Flow-D
    /// lookups) since this runs on the login critical path, where a strict 200ms deadline would
    /// routinely miss under load and silently degrade every login to single-tenant data.
    /// </summary>
    public async Task<List<UsersTenant>> GetMembershipsAsync(Guid userId, int deadlineMilliseconds = 500)
    {
        var cached = await _cacheService.GetAsync<List<UsersTenant>>(CacheKey(userId));
        if (cached != null && cached.Count > 0)
        {
            return await ApplyRequestStateAsync(cached, userId);
        }

        var lookup = await _membershipGrpcClient.GetActiveMembershipsAsync(userId, deadlineMilliseconds);
        if (!lookup.Success)
        {
            Log.Logger.Warning("MembershipCacheService: gRPC lookup failed for user {UserId} -- falling back to DefaultTenant* fields", userId);
            return await FallbackToDefaultTenantAsync(userId);
        }

        var tenants = lookup.Memberships.Select(m => new UsersTenant
        {
            TenantId = m.TenantId,
            Name = m.TenantName ?? "",
            Roles = m.Roles,
            Permissions = m.Permissions,
            State = m.Status
        }).ToList();

        Log.Logger.Information("MembershipCacheService: resolved {Count} membership(s) for user {UserId} via gRPC", tenants.Count, userId);

        var withState = await ApplyRequestStateAsync(tenants, userId);
        await _cacheService.SetAsync(CacheKey(userId), withState, CacheDuration);
        return withState;
    }

    public async Task InvalidateAsync(Guid userId)
    {
        await _cacheService.RemoveAsync(CacheKey(userId));
    }

    private async Task<List<UsersTenant>> FallbackToDefaultTenantAsync(Guid userId)
    {
        var user = await _uow.Context.Users.FindAsync(userId);
        if (user?.DefaultTenantId == null || user.DefaultTenantId == Guid.Empty)
        {
            Log.Logger.Warning("MembershipCacheService fallback: user {UserId} has no DefaultTenantId either -- returning zero memberships", userId);
            return new List<UsersTenant>();
        }

        Log.Logger.Warning(
            "MembershipCacheService fallback: user {UserId} tenant {TenantId} roles=[{Roles}] -- permissions are EMPTY in this fallback path",
            userId, user.DefaultTenantId, string.Join(",", user.DefaultTenantRoles ?? []));

        return new List<UsersTenant>
        {
            new()
            {
                TenantId = user.DefaultTenantId.Value,
                Name = user.DefaultTenantName ?? "",
                Roles = user.DefaultTenantRoles ?? [],
                State = TenantCreationStatus.Provisioning.ToString()
            }
        };
    }

    /// <summary>
    /// Merges in Auth-local TenantCreationRequestStatus so a tenant THIS user themselves just
    /// requested, which Tenant Service doesn't know about yet, still shows up correctly (still
    /// "Provisioning") in their own tenant list.
    ///
    /// Scoped to requests this same userId made (TenantCreationRequestStatus.UserId == userId)
    /// -- a TenantCreationRequestStatus row exists once per TENANT (whoever originally created
    /// it), not once per member. Before this filter, an invited member's own correct, already-
    /// "Active" membership State (from the gRPC lookup above) got silently overwritten with the
    /// tenant CREATOR's unrelated TenantCreationStatus (e.g. "Created") for every tenant that
    /// had ever gone through the create-workspace flow -- effectively every tenant. State no
    /// longer reflected this user's own membership standing once that happened.
    ///
    /// HrDbStatus/HrDbReady are the exception: HR-resource provisioning is a property of the
    /// TENANT, not of whoever requested it, so they're applied from the tenant's request row
    /// for every member. Scoping them to the requester too left every invited member with
    /// HrDbReady=false forever, which kept the app shell on the "Setting up your company"
    /// screen for a company that was long since ready.
    /// </summary>
    private async Task<List<UsersTenant>> ApplyRequestStateAsync(List<UsersTenant> tenants, Guid userId)
    {
        var ids = tenants.Select(t => t.TenantId).ToList();
        // Materialized before grouping -- only one request row exists per tenant in practice,
        // and grouping client-side keeps this translatable on every EF provider.
        var requestRows = await _uow.Context.TenantCreationRequests
            .Where(x => ids.Contains(x.TenantId))
            .ToListAsync();
        var requestsByTenant = requestRows
            .GroupBy(x => x.TenantId)
            .ToDictionary(x => x.Key, x => x.ToList());

        foreach (var tenant in tenants)
        {
            if (!requestsByTenant.TryGetValue(tenant.TenantId, out var rows)) continue;

            var tenantRow = rows[0];
            tenant.HrDbStatus = tenantRow.HrDbStatus;
            tenant.HrDbReady = tenantRow.HrDbReady;

            var ownRequest = rows.FirstOrDefault(x => x.UserId == userId);
            if (string.IsNullOrEmpty(tenant.Name))
                tenant.Name = ownRequest?.TenantName ?? tenantRow.TenantName ?? "";
            if (ownRequest != null)
                tenant.State = ownRequest.Status.ToString();
        }
        return tenants;
    }
}
