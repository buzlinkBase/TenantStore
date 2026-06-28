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
        var createTenant = new TenantCreationRequest
        {
            TenantId = Guid.NewGuid(),
            UserId = user.Id,
            TenantName = payload.TenantName ?? user.FullName ?? string.Concat(user.Email?.Split('@')[0] ?? "My", " Workspace"),
        };
        //store request
        var request = new TenantCreationRequestStatus
        {
            TenantId = createTenant.TenantId,
            Status = TenantCreationStatus.Provisioning,
            UserId = user.Id,
        };
        _tenantProvider.SetTenantId(createTenant.TenantId);
        await _requestService.Store(request);

        //generate token
        await _publisher.Publish(createTenant, token);

        var accessToken = await _jwtService.CreateTokenAsync(user, createTenant.TenantId.ToString(), createTenant.TenantName);
        var refreshTokenString = await _jwtService.GenerateRefreshToken();
        await Context.RefreshTokens.AddAsync(_userService.CreateRefreshToken(user, refreshTokenString), token);
        await CommitChangesAsync(token);
        return _userService.ComposeLoginRespose(user, accessToken, refreshTokenString);
    }
}
