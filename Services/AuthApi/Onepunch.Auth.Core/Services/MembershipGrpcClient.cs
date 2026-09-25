using Grpc.Core;
using Serilog;
using System.Diagnostics;

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
        var sw = Stopwatch.StartNew();
        try
        {
            var request = new UserMembershipsRequest { UserId = userId.ToString() };
            var response = await _client.GetActiveMembershipsAsync(
                request,
                deadline: DateTime.UtcNow.AddMilliseconds(deadlineMilliseconds));

            Log.Logger.Information(
                "MembershipGrpcClient.GetActiveMembershipsAsync succeeded for user {UserId} in {ElapsedMs}ms -- {Count} membership(s)",
                userId, sw.ElapsedMilliseconds, response.Memberships.Count);

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
            Log.Logger.Warning(ex, "MembershipGrpcClient.GetActiveMembershipsAsync failed for user {UserId} after {ElapsedMs}ms: {Status}", userId, sw.ElapsedMilliseconds, ex.StatusCode);
            return new MembershipLookupResult { Success = false };
        }
        // RpcException only covers gRPC-status-level failures -- a connection refused, DNS
        // failure, or TLS handshake error surfaces as a plain Exception instead, and without
        // this would go completely unlogged while also risking propagating uncaught out of the
        // whole login/token-mint call chain instead of degrading gracefully like the RpcException
        // case above.
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "MembershipGrpcClient.GetActiveMembershipsAsync failed for user {UserId} after {ElapsedMs}ms with a non-RPC exception", userId, sw.ElapsedMilliseconds);
            return new MembershipLookupResult { Success = false };
        }
    }

    /// <summary>
    /// Synchronously activates (or creates) the user's membership on a tenant and returns its
    /// resolved roles/permissions -- see Tenant Service's TenantInfoServiceProvider.ActivateMembership.
    /// Virtual so InvitationService tests can stub it without a live gRPC channel.
    /// </summary>
    public virtual async Task<MembershipResolveResult> ActivateMembershipAsync(
        Guid userId,
        Guid tenantId,
        string? tenantName,
        string? email,
        string? fullName,
        IEnumerable<string> roles,
        int deadlineMilliseconds = 2000)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var request = new ActivateMembershipRequest
            {
                UserId = userId.ToString(),
                TenantId = tenantId.ToString(),
                TenantName = tenantName ?? string.Empty,
                Email = email ?? string.Empty,
                FullName = fullName ?? string.Empty,
            };
            request.Roles.AddRange(roles);
            var response = await _client.ActivateMembershipAsync(
                request,
                deadline: DateTime.UtcNow.AddMilliseconds(deadlineMilliseconds));

            Log.Logger.Information(
                "MembershipGrpcClient.ActivateMembershipAsync succeeded for user {UserId}/tenant {TenantId} in {ElapsedMs}ms -- found={Found}, {PermissionCount} permission(s)",
                userId, tenantId, sw.ElapsedMilliseconds, response.Found, response.Permissions.Count);

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
            Log.Logger.Warning(ex, "MembershipGrpcClient.ActivateMembershipAsync failed for user {UserId}/tenant {TenantId} after {ElapsedMs}ms: {Status}", userId, tenantId, sw.ElapsedMilliseconds, ex.StatusCode);
            return new MembershipResolveResult { Success = false };
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "MembershipGrpcClient.ActivateMembershipAsync failed for user {UserId}/tenant {TenantId} after {ElapsedMs}ms with a non-RPC exception", userId, tenantId, sw.ElapsedMilliseconds);
            return new MembershipResolveResult { Success = false };
        }
    }

    public async Task<MembershipResolveResult> ResolveMembershipAsync(Guid userId, Guid tenantId, int deadlineMilliseconds = 200)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var request = new MembershipRequest { UserId = userId.ToString(), TenantId = tenantId.ToString() };
            var response = await _client.ResolveMembershipAsync(
                request,
                deadline: DateTime.UtcNow.AddMilliseconds(deadlineMilliseconds));

            Log.Logger.Information(
                "MembershipGrpcClient.ResolveMembershipAsync succeeded for user {UserId}/tenant {TenantId} in {ElapsedMs}ms -- found={Found}",
                userId, tenantId, sw.ElapsedMilliseconds, response.Found);

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
            Log.Logger.Warning(ex, "MembershipGrpcClient.ResolveMembershipAsync failed for user {UserId}/tenant {TenantId} after {ElapsedMs}ms: {Status}", userId, tenantId, sw.ElapsedMilliseconds, ex.StatusCode);
            return new MembershipResolveResult { Success = false };
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "MembershipGrpcClient.ResolveMembershipAsync failed for user {UserId}/tenant {TenantId} after {ElapsedMs}ms with a non-RPC exception", userId, tenantId, sw.ElapsedMilliseconds);
            return new MembershipResolveResult { Success = false };
        }
    }
}
