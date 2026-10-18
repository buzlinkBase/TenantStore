using Grpc.Core;
using Onepunch.Common.Lib.Exceptions;
using Onepunch.Common.Lib.Subscriptions;
using Serilog;
using System.Diagnostics;

namespace Onepunch.Auth.Core.Services;

public class MembershipLookupResult
{
    public bool Success { get; set; }
    public List<AccountMemberShipQuery> Memberships { get; set; } = new();
}

/// <summary>Tenant Service's answer to "may this person invite this email to this client?".</summary>
public sealed record ClientInviteCheck(bool Allowed, string? Error, Guid TenantId, string TenantName, string ClientName);

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

    /// <summary>
    /// Whether this member may grant these roles (Tenant Service's RoleGrantPolicy: no role with
    /// permissions they don't hold themselves). Fails closed: if Tenant Service can't answer, the
    /// grant is refused. Virtual so InvitationService tests can stub it.
    /// </summary>
    public virtual async Task<(bool Allowed, string? Error)> CheckRoleGrantAsync(
        Guid userId, Guid tenantId, IEnumerable<string> roles, int deadlineMilliseconds = 2000)
    {
        try
        {
            var request = new RoleGrantRequest { UserId = userId.ToString(), TenantId = tenantId.ToString() };
            request.Roles.AddRange(roles);
            var response = await _client.CheckRoleGrantAsync(
                request,
                deadline: DateTime.UtcNow.AddMilliseconds(deadlineMilliseconds));
            return (response.Allowed, response.Allowed ? null : response.Error);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "MembershipGrpcClient.CheckRoleGrantAsync failed for user {UserId}/tenant {TenantId}", userId, tenantId);
            return (false, "The role couldn't be checked right now. Please try again.");
        }
    }

    /// <summary>
    /// Asks Tenant Service whether a subscription limit allows one more (or <paramref name="requested"/>
    /// more) -- companies.max for an account, admins.max for a company. Throws the refusal as a
    /// <see cref="PlanRestrictionException"/>. Fails closed: if Tenant Service can't answer, the
    /// action is refused rather than let past a limit. Virtual so service tests can stub it.
    /// </summary>
    public virtual async Task EnsurePlanAllowsAsync(
        string limitKey, Guid? userId, Guid? tenantId, int requested = 1, int deadlineMilliseconds = 2000)
    {
        PlanAllowanceResponse response;
        try
        {
            response = await _client.CheckPlanAllowanceAsync(
                new PlanAllowanceRequest
                {
                    LimitKey = limitKey,
                    UserId = userId?.ToString() ?? string.Empty,
                    TenantId = tenantId?.ToString() ?? string.Empty,
                    Requested = requested,
                },
                deadline: DateTime.UtcNow.AddMilliseconds(deadlineMilliseconds));
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "MembershipGrpcClient.EnsurePlanAllowsAsync({Limit}) failed for user {UserId}/tenant {TenantId}", limitKey, userId, tenantId);
            throw new GuardException("Your plan couldn't be checked right now. Please try again.");
        }

        if (response.Allowed) return;

        throw new PlanRestrictionException(
            string.IsNullOrEmpty(response.ProblemType) ? SubscriptionProblemTypes.PlanLimitReached : response.ProblemType,
            string.IsNullOrEmpty(response.Error) ? "Your plan's limit is reached." : response.Error,
            new Dictionary<string, object?>
            {
                ["limit"] = limitKey,
                ["max"] = response.Unlimited ? null : response.Max,
                ["used"] = response.Used,
            });
    }

    /// <summary>
    /// Whether this person may invite this email to a client with these roles (Tenant Service owns who
    /// holds what in a client) -- and, if so, the company and client names to word the invitation with.
    /// Fails closed. Virtual so tests can stub it.
    /// </summary>
    public virtual async Task<ClientInviteCheck> CheckClientInviteAsync(
        Guid inviterUserId, Guid clientEntityId, string email, IEnumerable<Guid> roleIds, int deadlineMilliseconds = 2000)
    {
        try
        {
            var request = new CheckClientInviteRequest
            {
                InviterUserId = inviterUserId.ToString(),
                ClientEntityId = clientEntityId.ToString(),
                Email = email,
            };
            request.RoleIds.AddRange(roleIds.Select(r => r.ToString()));
            var response = await _client.CheckClientInviteAsync(request, deadline: DateTime.UtcNow.AddMilliseconds(deadlineMilliseconds));
            return response.Allowed
                ? new ClientInviteCheck(true, null, Guid.Parse(response.TenantId), response.TenantName, response.ClientName)
                : new ClientInviteCheck(false, response.Error, Guid.Empty, string.Empty, string.Empty);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "MembershipGrpcClient.CheckClientInviteAsync failed for user {UserId}/client {ClientId}", inviterUserId, clientEntityId);
            return new ClientInviteCheck(false, "The invitation couldn't be checked right now. Please try again.", Guid.Empty, string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// Makes the user a client user of the client with the invited roles (invitation acceptance).
    /// Idempotent on Tenant Service's side, so a retry after a failure is safe. Virtual so tests can stub it.
    /// </summary>
    public virtual async Task<(bool Ok, string? Error)> GrantClientAccessAsync(
        Guid userId, Guid clientEntityId, string? email, string? fullName, IEnumerable<Guid> roleIds, int deadlineMilliseconds = 5000)
    {
        try
        {
            var request = new GrantClientAccessRequest
            {
                UserId = userId.ToString(),
                ClientEntityId = clientEntityId.ToString(),
                Email = email ?? string.Empty,
                FullName = fullName ?? string.Empty,
            };
            request.RoleIds.AddRange(roleIds.Select(r => r.ToString()));
            var response = await _client.GrantClientAccessAsync(request, deadline: DateTime.UtcNow.AddMilliseconds(deadlineMilliseconds));
            return (response.Ok, response.Ok ? null : response.Error);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "MembershipGrpcClient.GrantClientAccessAsync failed for user {UserId}/client {ClientId}", userId, clientEntityId);
            return (false, "Access couldn't be granted right now. Please try again.");
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
