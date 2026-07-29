using Grpc.Core;
using Onepunch.Gateway.Api.Protos;
using Onepunch.Common.Lib.Cache;

namespace Onepunch.Gateway.Api.Services;

public class RoleCheckResult
{
    public bool Found { get; set; }
    public List<string> Roles { get; set; } = new();
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Flow D: defense-in-depth revocation check at the request-authorization layer. Deliberately
/// separate from Auth's 5-minute mint-time membership snapshot (MembershipCacheService) — this
/// cache is short-TTL (30-60s) and exists only to catch a role/membership change that happened
/// after a token was minted but before it expires; it is a backstop, not the primary
/// invalidation mechanism (that's Flow H's MembershipChanged-driven eviction on the Auth side).
/// </summary>
public class GatewayMembershipClient
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(45);

    private readonly ICacheService _cacheService;
    private readonly GetTenantService.GetTenantServiceClient _grpcClient;

    public GatewayMembershipClient(ICacheService cacheService, GetTenantService.GetTenantServiceClient grpcClient)
    {
        _cacheService = cacheService;
        _grpcClient = grpcClient;
    }

    private static string CacheKey(Guid userId, Guid tenantId) => $"gw:role:{userId}:{tenantId}";

    public async Task<RoleCheckResult?> CheckAsync(Guid userId, Guid tenantId, int deadlineMilliseconds = 200)
    {
        var cached = await _cacheService.GetAsync<RoleCheckResult>(CacheKey(userId, tenantId));
        if (cached != null) return cached;

        try
        {
            var request = new MembershipRequest { UserId = userId.ToString(), TenantId = tenantId.ToString() };
            var response = await _grpcClient.ResolveMembershipAsync(
                request,
                deadline: DateTime.UtcNow.AddMilliseconds(deadlineMilliseconds));

            var result = new RoleCheckResult
            {
                Found = response.Found,
                Roles = response.Roles.ToList(),
                Status = response.Status
            };
            await _cacheService.SetAsync(CacheKey(userId, tenantId), result, CacheDuration);
            return result;
        }
        catch (RpcException)
        {
            // Tenant Service unreachable/slow: return null so the caller can decide whether to
            // fail closed or fall back to the token's own claims rather than hard-failing here.
            return null;
        }
    }
}
