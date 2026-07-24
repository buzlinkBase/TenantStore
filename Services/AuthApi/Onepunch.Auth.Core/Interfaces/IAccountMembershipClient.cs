using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib.Cache;
using OnePunch.Auth.Core;
using Refit;

namespace Onepunch.Auth.Core.Interfaces;

public interface IAccountMembershipClient
{
    [Get("/api/v1/members/account-tenants")]
    [Headers("Accept: application/x-msgpack")]
    Task<ResponseModel<List<AccountMemberShipQuery>>> FindTenants(
         [Query][AliasAs("user-id")] Guid userId,
        [Header("Authorization")] string authorization);
}

public class AccountTenantsProvider
{
    private readonly ICacheService _cacheService;
    private readonly IUnitOfWorkService _uow;
    private readonly IAccountMembershipClient _membershipClient;

    public AccountTenantsProvider(ICacheService cacheService,
        IUnitOfWorkService uow,
        IAccountMembershipClient membershipClient)
    {
        _cacheService = cacheService;
        _uow = uow;
        _membershipClient = membershipClient;
    }

    public async Task<List<UsersTenant>> FindTenants(Guid userId, string accessToken)
    {
        var key = $"{userId}:tenantQuery";
        var cache = await _cacheService.GetAsync<List<UsersTenant>>(key);
        var tenants = cache;
        if (tenants == null || !tenants.Any())
        {
            tenants = await LoadFromTenantService(userId, accessToken);
            await _cacheService.SetAsync(key, tenants, TimeSpan.FromMinutes(30));
            return tenants;
        }
        var requestStates = await GetRequestStates(tenants.Select(x => x.TenantId));
        foreach (var tenant in tenants)
        {
            requestStates.TryGetValue(tenant.TenantId, out var stateData);
            tenant.Name = ResolveName(tenant.Name, stateData);
            tenant.State = ResolveStatus(stateData).ToString();
        }
        return tenants;
    }

    private async Task<List<UsersTenant>> LoadFromTenantService(Guid userId, string accessToken)
    {
        var authorizationHeader = $"Bearer {accessToken}";
        var members = await _membershipClient.FindTenants(userId, authorizationHeader);

        if (members?.Data == null || !members.Data.Any())
            return new List<UsersTenant>();

        var requestStates = await GetRequestStates(members.Data.Select(x => x.TenantId));

        return members.Data.Select(x =>
        {
            requestStates.TryGetValue(x.TenantId, out var stateData);
            return new UsersTenant
            {
                TenantId = x.TenantId,
                Name = ResolveName(x.TenantName, stateData),
                State = ResolveStatus(stateData).ToString(),
                Role = x.Role ?? ""
            };
        }).ToList();
    }

    private async Task<Dictionary<Guid, TenantCreationRequestStatus>> GetRequestStates(IEnumerable<Guid> tenantIds)
    {
        var ids = tenantIds.ToList();
        return await _uow.Context.TenantCreationRequests
            .Where(x => ids.Contains(x.TenantId))
            .GroupBy(x => x.TenantId)
            .ToDictionaryAsync(x => x.Key, x => x.First());
    }

    private static string ResolveName(string? primaryName, TenantCreationRequestStatus? stateData)
    {
        if (!string.IsNullOrEmpty(primaryName))
            return primaryName;

        return stateData?.TenantName ?? "";
    }

    private static TenantCreationStatus ResolveStatus(TenantCreationRequestStatus? stateData)
    {
        return stateData?.Status ?? TenantCreationStatus.Provisioning;
    }
}