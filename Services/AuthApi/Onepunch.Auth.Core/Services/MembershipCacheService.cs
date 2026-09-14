using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib.Cache;
using OnePunch.Auth.Core;

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
            return new List<UsersTenant>();

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
    /// </summary>
    private async Task<List<UsersTenant>> ApplyRequestStateAsync(List<UsersTenant> tenants, Guid userId)
    {
        var ids = tenants.Select(t => t.TenantId).ToList();
        var requestStates = await _uow.Context.TenantCreationRequests
            .Where(x => ids.Contains(x.TenantId) && x.UserId == userId)
            .GroupBy(x => x.TenantId)
            .ToDictionaryAsync(x => x.Key, x => x.First());

        foreach (var tenant in tenants)
        {
            requestStates.TryGetValue(tenant.TenantId, out var stateData);
            if (string.IsNullOrEmpty(tenant.Name))
                tenant.Name = stateData?.TenantName ?? "";
            if (stateData != null)
            {
                tenant.State = stateData.Status.ToString();
                tenant.HrDbStatus = stateData.HrDbStatus;
                tenant.HrDbReady = stateData.HrDbReady;
            }
        }
        return tenants;
    }
}
