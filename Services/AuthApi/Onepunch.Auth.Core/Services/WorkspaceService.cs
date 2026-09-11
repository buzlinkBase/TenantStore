using MassTransit;
using Microsoft.AspNetCore.Identity;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using System.Security.Claims;

namespace Onepunch.Auth.Core.Services;

public class WorkspaceService : BaseService<User>
{
    private readonly IPublishEndpoint _publisher;
    private readonly UserService _userService;
    private readonly TenantRequestService _requestService;
    private readonly UserManager<User> _userManager;
    private readonly JwtService _jwtService;
    private readonly ITenantProvider _tenantProvider;
    public WorkspaceService(
         IPublishEndpoint publisher,
         UserService userService,
         TenantRequestService requestService,
         IUnitOfWorkService uow,
         UserManager<User> userManager,
         JwtService jwtService,
         ITenantProvider tenantProvider) : base(uow)
    {
        _publisher = publisher;
        _userService = userService;
        _requestService = requestService;
        _userManager = userManager;
        _jwtService = jwtService;
        _tenantProvider = tenantProvider;
    }

    public async Task<LoginResponse> Create(CreateWorkspaceRequest payload, ClaimsPrincipal contextUser, CancellationToken token)
    {
        var user = await _userManager.GetUserAsync(contextUser);
        if (user == null) throw new UnauthorizedException();

        // A refresh (or double-submit) mid-setup used to land back on the same blank form with
        // no memory of the in-flight request, so resubmitting fired a brand-new
        // TenantCreationRequested every time -- each one a separate dormant/stuck-Provisioning
        // tenant the user never intended. Resume the existing request instead of starting
        // another one; it naturally falls out of scope once RequestExpiry passes (same 1-day
        // window InspectTenantRequestState already uses to give up on a truly-stuck request).
        var pending = await _requestService.FindPendingByUser(user.Id);
        if (pending != null)
        {
            return await BuildResumeResponse(user, pending, token);
        }

        var createTenant = new TenantCreationRequested
        {
            TenantId = Guid.NewGuid(),
            Email = user.Email ?? "",
            FullName = user.FullName ?? "Account Owner",
            UserId = user.Id,
            TenantName = payload.TenantName ?? user.FullName ?? string.Concat(user.Email?.Split('@')[0] ?? "My", " Workspace"),
        };
        //store request
        var request = new TenantCreationRequestStatus
        {
            TenantId = createTenant.TenantId,
            Status = TenantCreationStatus.Provisioning,
            UserId = user.Id,
            TenantName = createTenant.TenantName,
            RequestExpiry = DateTime.UtcNow.AddDays(1),
        };
        _tenantProvider.SetTenantId(createTenant.TenantId);
        await _requestService.Store(request);

        //generate token
        await _publisher.Publish(createTenant, token);

        var accessToken = await _jwtService.CreateTokenAsync(user, createTenant.TenantId.ToString(), createTenant.TenantName);
        var refreshTokenString = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(_userService.CreateRefreshToken(user, refreshTokenString), token);
        await CommitChangesAsync(token);
        var loginRequest = await _userService.ComposeLoginResponse(user, accessToken, refreshTokenString);
        loginRequest.Tenants.Add(new UsersTenant
        {
            Name = request.TenantName,
            Roles = ["Owner"],
            State = TenantCreationStatus.Provisioning.ToString(),
            TenantId = createTenant.TenantId,
            HrDbReady = false,
        });

        // ComposeLoginResponse's top-level Roles/Permissions reflect user.DefaultTenantRoles --
        // the caller's *home* tenant, not the brand-new one this response just switched them
        // into (its real Owner membership doesn't exist in TenantApi yet; it's only just been
        // published as a TenantCreationRequested event). Left as-is, a caller whose default
        // tenant role is "Employee" would get a response claiming they're still Employee-only
        // right after creating a workspace they in fact own -- e.g. tripping the frontend's
        // Employee-only route guard and bouncing them back to the portal instead of /dashboard.
        // Override to match the Owner entry just appended above.
        loginRequest.Roles = ["Owner"];
        loginRequest.Permissions = [];

        return loginRequest;
    }

    /// <summary>
    /// Mints a session scoped to an already-in-flight tenant creation request instead of firing
    /// another one -- same tail as Create() (token mint, Owner entry, Roles/Permissions
    /// override) but reading the existing request's TenantId/TenantName/Status rather than
    /// generating a new TenantId or re-publishing TenantCreationRequested.
    /// </summary>
    private async Task<LoginResponse> BuildResumeResponse(User user, TenantCreationRequestStatus pending, CancellationToken token)
    {
        _tenantProvider.SetTenantId(pending.TenantId);

        var accessToken = await _jwtService.CreateTokenAsync(user, pending.TenantId.ToString(), pending.TenantName ?? "");
        var refreshTokenString = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(_userService.CreateRefreshToken(user, refreshTokenString), token);
        await CommitChangesAsync(token);
        var loginRequest = await _userService.ComposeLoginResponse(user, accessToken, refreshTokenString);
        loginRequest.Tenants.Add(new UsersTenant
        {
            Name = pending.TenantName,
            Roles = ["Owner"],
            State = pending.Status.ToString(),
            TenantId = pending.TenantId,
            HrDbReady = pending.HrDbReady,
        });

        loginRequest.Roles = ["Owner"];
        loginRequest.Permissions = [];

        return loginRequest;
    }
}
