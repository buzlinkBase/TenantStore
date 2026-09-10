using Grpc.Core;
using Onepunch.Auth.Core.Protos;
using Serilog;

namespace Onepunch.Auth.Core.Services;

public class MembershipLookupResult
{
    public bool Success { get; set; }
    public List<AccountMemberShipQuery> Memberships { get; set; } = new();
}

public class MembershipResolveResult
{
    public bool Success { get; set; }
    public bool Found { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
}

/// <summary>
/// Wraps Tenant Service's membership-resolution gRPC RPCs. Failures (timeout/unavailable) are
/// caught and surfaced as a typed, non-success result rather than thrown, so callers (token
/// issuance, tenant switch) can degrade gracefully instead of failing the whole request.
/// </summary>
public class MembershipGrpcClient
{
    private readonly GetTenantService.GetTenantServiceClient _client;

    public MembershipGrpcClient(GetTenantService.GetTenantServiceClient client)
    {
        _client = client;
    }

    public async Task<MembershipLookupResult> GetActiveMembershipsAsync(Guid userId, int deadlineMilliseconds = 200)
    {
        try
        {
            var request = new UserMembershipsRequest { UserId = userId.ToString() };
            var response = await _client.GetActiveMembershipsAsync(
                request,
                deadline: DateTime.UtcNow.AddMilliseconds(deadlineMilliseconds));

            return new MembershipLookupResult
            {
                Success = true,
                Memberships = response.Memberships.Select(m => new AccountMemberShipQuery
                {
                    TenantId = Guid.Parse(m.TenantId),
                    UserId = userId,
                    TenantName = m.TenantName,
                    Roles = m.Roles.ToList(),
                    Permissions = m.Permissions.ToList(),
                    Status = m.Status
                }).ToList()
            };
        }
        catch (RpcException ex)
        {
            Log.Logger.Warning(ex, "MembershipGrpcClient.GetActiveMembershipsAsync failed for user {UserId}: {Status}", userId, ex.StatusCode);
            return new MembershipLookupResult { Success = false };
        }
    }

    public async Task<MembershipResolveResult> ResolveMembershipAsync(Guid userId, Guid tenantId, int deadlineMilliseconds = 200)
    {
        try
        {
            var request = new MembershipRequest { UserId = userId.ToString(), TenantId = tenantId.ToString() };
            var response = await _client.ResolveMembershipAsync(
                request,
                deadline: DateTime.UtcNow.AddMilliseconds(deadlineMilliseconds));

            return new MembershipResolveResult
            {
                Success = true,
                Found = response.Found,
                Roles = response.Roles.ToList(),
                Permissions = response.Permissions.ToList(),
                Status = response.Status,
                TenantName = response.TenantName
            };
        }
        catch (RpcException ex)
        {
            Log.Logger.Warning(ex, "MembershipGrpcClient.ResolveMembershipAsync failed for user {UserId}/tenant {TenantId}: {Status}", userId, tenantId, ex.StatusCode);
            return new MembershipResolveResult { Success = false };
        }
    }
}
