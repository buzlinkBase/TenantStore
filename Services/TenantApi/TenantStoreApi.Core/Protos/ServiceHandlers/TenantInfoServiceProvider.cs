using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Onepunch.Auth.Core;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Core.Services.Subscriptions;

namespace TenantStoreApi.Core.Protos.ServiceHandlers;

/// <summary>
/// Internal service-to-service RPCs (Auth Service calling on its own authority, not forwarding
/// an end-user token — MembershipGrpcClient sends no credentials at all). Tenant Service's
/// global fallback policy requires an authenticated user on every endpoint by default, which
/// silently blocks these gRPC calls before they ever reach the handler methods below unless
/// exempted here. The gRPC port isn't exposed publicly, so network trust substitutes for a
/// per-call JWT.
/// </summary>
[AllowAnonymous]
public class TenantInfoServiceProvider : GetTenantService.GetTenantServiceBase
{
    private readonly TenantService _service;
    private readonly UserMembershipService _membershipService;
    private readonly RoleService _roleService;

    private readonly PlanAllowanceService _allowance;
    private readonly ClientAccessService _clientAccess;
    private readonly ClientEntityService _clientEntities;
    private readonly ClientMembershipService _clientMembers;

    public TenantInfoServiceProvider(
        TenantService service,
        UserMembershipService membershipService,
        RoleService roleService,
        PlanAllowanceService allowance,
        ClientAccessService clientAccess,
        ClientEntityService clientEntities,
        ClientMembershipService clientMembers)
    {
        _clientMembers = clientMembers;
        _service = service;
        _membershipService = membershipService;
        _roleService = roleService;
        _allowance = allowance;
        _clientAccess = clientAccess;
        _clientEntities = clientEntities;
    }

    public override async Task<ClientMembershipResponse> ResolveClientMembership(ClientMembershipRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || !Guid.TryParse(request.ClientEntityId, out var clientEntityId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for UserId/ClientEntityId"));
        }

        var access = await _clientAccess.ResolveAsync(userId, clientEntityId, context.CancellationToken);
        return access == null
            ? new ClientMembershipResponse { Found = false }
            : new ClientMembershipResponse { Found = true, Membership = ToEntry(access) };
    }

    public override async Task<ClientMembershipsResponse> ListClientMemberships(UserMembershipsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid UserId format"));
        }

        var response = new ClientMembershipsResponse();
        response.Memberships.AddRange((await _clientAccess.ListForUserAsync(userId, context.CancellationToken)).Select(ToEntry));
        return response;
    }

    /// <summary>The portal reports a client it created or changed. Committed here: this is a whole call, not part of a larger unit of work.</summary>
    public override async Task<UpsertClientEntityResponse> UpsertClientEntity(UpsertClientEntityRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TenantId, out var tenantId) || !Guid.TryParse(request.ClientEntityId, out var clientEntityId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for TenantId/ClientEntityId"));
        }

        try
        {
            await _clientEntities.UpsertAsync(
                tenantId, clientEntityId,
                string.IsNullOrWhiteSpace(request.ExternalId) ? null : request.ExternalId,
                request.Name, request.Active, context.CancellationToken);
            await _clientEntities.CommitChangesAsync(context.CancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }

        return new UpsertClientEntityResponse { Ok = true };
    }

    public override async Task<CheckClientInviteResponse> CheckClientInvite(CheckClientInviteRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.InviterUserId, out var inviterId) || !Guid.TryParse(request.ClientEntityId, out var clientEntityId)
            || !TryParseIds(request.RoleIds, out var roleIds))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for InviterUserId/ClientEntityId/RoleIds"));
        }

        try
        {
            var invite = await _clientMembers.AuthorizeInviteAsync(inviterId, clientEntityId, request.Email, roleIds, context.CancellationToken);
            return new CheckClientInviteResponse
            {
                Allowed = true,
                TenantId = invite.TenantId.ToString(),
                TenantName = invite.TenantName,
                ClientName = invite.ClientName,
            };
        }
        // Every refusal is an answer, not a fault: AuthApi shows the message to the inviter.
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            return new CheckClientInviteResponse { Allowed = false, Error = ex.Message };
        }
    }

    /// <summary>Invitation acceptance. Committed here: this is a whole call, not part of a larger unit of work.</summary>
    public override async Task<GrantClientAccessResponse> GrantClientAccess(GrantClientAccessRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || !Guid.TryParse(request.ClientEntityId, out var clientEntityId)
            || !TryParseIds(request.RoleIds, out var roleIds))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for UserId/ClientEntityId/RoleIds"));
        }

        try
        {
            await _clientMembers.GrantAccessAsync(userId, clientEntityId, request.Email, request.FullName, roleIds, context.CancellationToken);
            await _clientMembers.CommitChangesAsync(context.CancellationToken);
            return new GrantClientAccessResponse { Ok = true };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return new GrantClientAccessResponse { Ok = false, Error = ex.Message };
        }
    }

    private static bool TryParseIds(IEnumerable<string> raw, out List<Guid> ids)
    {
        ids = [];
        foreach (var value in raw)
        {
            if (!Guid.TryParse(value, out var id)) return false;
            ids.Add(id);
        }
        return true;
    }

    private static ClientMembershipEntry ToEntry(ClientAccess access)
    {
        var entry = new ClientMembershipEntry
        {
            ClientEntityId = access.ClientEntityId.ToString(),
            ClientName = access.ClientName,
            TenantId = access.TenantId.ToString(),
            TenantName = access.TenantName ?? string.Empty,
            Status = access.Status,
        };
        entry.Roles.AddRange(access.Roles);
        entry.Permissions.AddRange(access.Permissions);
        return entry;
    }

    /// <summary>
    /// Subscription limits Tenant Service owns -- AuthApi asks before creating a workspace
    /// (companies.max, per account) and before inviting admins (admins.max, per company).
    /// </summary>
    public override async Task<PlanAllowanceResponse> CheckPlanAllowance(PlanAllowanceRequest request, ServerCallContext context)
    {
        var allowance = request.LimitKey switch
        {
            EntitlementKeys.CompaniesMax when Guid.TryParse(request.UserId, out var userId) =>
                await _allowance.CheckCompanyAsync(userId, context.CancellationToken),
            EntitlementKeys.AdminsMax when Guid.TryParse(request.TenantId, out var tenantId) =>
                await _allowance.CheckAdminSeatsAsync(tenantId, Math.Max(1, request.Requested), context.CancellationToken),
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unsupported limit '{request.LimitKey}' or missing id")),
        };

        return new PlanAllowanceResponse
        {
            Allowed = allowance.Allowed,
            ProblemType = allowance.ProblemType ?? string.Empty,
            Error = allowance.Error ?? string.Empty,
            Max = allowance.Max ?? 0,
            Unlimited = allowance.Max == null,
            Used = allowance.Used,
        };
    }

    /// <summary>
    /// Whether a company may be changed at all (read-only or operator-stopped companies may not) --
    /// AuthApi asks before every invitation.
    /// </summary>
    public override async Task<PlanAllowanceResponse> CheckCompanyWritable(CompanyWritableRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TenantId, out var tenantId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for TenantId"));

        var allowance = await _allowance.CheckWritableAsync(tenantId, context.CancellationToken);
        return new PlanAllowanceResponse
        {
            Allowed = allowance.Allowed,
            ProblemType = allowance.ProblemType ?? string.Empty,
            Error = allowance.Error ?? string.Empty,
            Unlimited = true,
        };
    }

    /// <summary>
    /// Whether the inviting member may grant these roles (RoleGrantPolicy) -- AuthApi asks before
    /// sending an invitation with a role other than the default, since only this service knows
    /// what each role grants. An unknown or inactive caller may grant nothing.
    /// </summary>
    public override async Task<RoleGrantResponse> CheckRoleGrant(RoleGrantRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || !Guid.TryParse(request.TenantId, out var tenantId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for UserId/TenantId"));
        }

        var caller = await _membershipService.GetMemberAsync(userId, tenantId, context.CancellationToken);
        if (caller == null || caller.Status != "Active")
            return new RoleGrantResponse { Allowed = false, Error = "You are not an active member of this company." };

        try
        {
            await _roleService.EnsureCanGrantAsync(caller, request.Roles, tenantId, context.CancellationToken);
            return new RoleGrantResponse { Allowed = true };
        }
        catch (InvalidOperationException ex)
        {
            return new RoleGrantResponse { Allowed = false, Error = ex.Message };
        }
    }

    public override async Task<TenantInfoResponse> GetInfo(TenantRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TenantId, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for TenantId"));
        }
        var tenant = await _service.FindTenantAsync(id, context.CancellationToken);
        if (tenant == null)
        {
            // This is the standard way to signal a missing resource in gRPC
            throw new RpcException(new Status(StatusCode.NotFound, $"Tenant with ID {id} not found"));
        }
        var response = new TenantInfoResponse
        {
            Name = tenant.TenantName,
            TenantId = tenant.Id.ToString(),
        };
        return response;
    }

    public override async Task<MembershipResponse> ResolveMembership(MembershipRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || !Guid.TryParse(request.TenantId, out var tenantId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for UserId/TenantId"));
        }

        var membership = await _membershipService.GetMemberAsync(userId, tenantId, context.CancellationToken);
        return ToMembershipResponse(membership);
    }

    /// <summary>
    /// Invitation acceptance (Auth's InvitationService.FinalizeAcceptanceAsync) calls this
    /// BEFORE minting the invitee's tenant-scoped token. The UserJoin/InvitationAccepted events
    /// Auth also publishes only leave its outbox after its own commit -- i.e. after the token is
    /// already minted -- so without this synchronous step the token and login response carried
    /// no permissions for the joined tenant, leaving the frontend's permission guards bouncing
    /// the user between routes. JoinAsync is idempotent, so those later events become no-ops.
    /// </summary>
    public override async Task<MembershipResponse> ActivateMembership(ActivateMembershipRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || !Guid.TryParse(request.TenantId, out var tenantId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for UserId/TenantId"));
        }

        if (request.Mode == ActivationMode.Legacy)
        {
            await _membershipService.JoinAsync(
                userId,
                tenantId,
                request.TenantName,
                request.Email,
                request.FullName,
                request.Roles.ToList(),
                context.CancellationToken);
        }
        else
        {
            // Invitation flow: refuse (changing nothing) when the invite was revoked or removed.
            var (accepted, denyReason) = await _membershipService.AcceptInviteAsync(
                userId,
                tenantId,
                request.TenantName,
                request.Email,
                request.FullName,
                request.Roles.ToList(),
                request.Mode == ActivationMode.Replay,
                context.CancellationToken);
            if (accepted == null)
                return new MembershipResponse { Found = false, Denied = true, DenyReason = denyReason ?? string.Empty };
        }
        await _membershipService.CommitChangesAsync(context.CancellationToken);

        // Re-read with the full role->permission graph loaded -- JoinAsync's returned entity
        // doesn't have RoleRef/RolePermissions populated for a newly created/activated row.
        var membership = await _membershipService.GetMemberAsync(userId, tenantId, context.CancellationToken);
        return ToMembershipResponse(membership);
    }

    private static MembershipResponse ToMembershipResponse(UserMembership? membership)
    {
        if (membership == null)
        {
            return new MembershipResponse { Found = false };
        }

        var response = new MembershipResponse
        {
            Found = true,
            Status = membership.Status,
            TenantName = membership.TenantName ?? string.Empty
        };
        response.Roles.AddRange(membership.RoleNames());
        response.Permissions.AddRange(membership.EffectivePermissionCodes());
        return response;
    }

    public override async Task<UserMembershipsResponse> GetActiveMemberships(UserMembershipsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid UserId format"));
        }

        var memberships = await _membershipService.GetUserMembersAsync(userId, context.CancellationToken);
        var response = new UserMembershipsResponse();
        response.Memberships.AddRange(memberships
            .Where(m => m.Status == "Active")
            .Select(m =>
            {
                var entry = new MembershipEntry
                {
                    TenantId = m.TenantId.ToString(),
                    TenantName = m.TenantName ?? string.Empty,
                    Status = m.Status
                };
                entry.Roles.AddRange(m.Roles);
                entry.Permissions.AddRange(m.Permissions);
                return entry;
            }));
        return response;
    }
}
