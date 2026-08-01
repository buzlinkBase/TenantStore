using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Onepunch.Auth.Core;
using TenantStoreApi.Core.Services;

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

    public TenantInfoServiceProvider(TenantService service, UserMembershipService membershipService)
    {
        _service = service;
        _membershipService = membershipService;
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
        return response;
    }

    public override async Task<UserMembershipsResponse> GetActiveMemberships(UserMembershipsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid GUID format for UserId"));
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
                return entry;
            }));
        return response;
    }
}
