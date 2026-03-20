using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OnePunch.Auth.Core;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Services;

public class TenantService
{
    private readonly GetTenantService.GetTenantServiceClient _client;
    private readonly ITenantProvider _tenantProvider;
    public TenantService(GetTenantService.GetTenantServiceClient client )
    {
        _client = client;
    }

    public async Task<TenantInfoResponse> GetInfoAsync()
    {
        var request = new TenantRequest { TenantId = _tenantProvider.TenantId.ToString() };
        return await _client.GetInfoAsync(request);
    }

    public async Task<TenantInfoResponse> GetGrpcBgInfoAsync()
    {
        var request = new TenantRequest { TenantId = _tenantProvider.TenantId.ToString() };
        return await _client.GetInfoAsync(request);
    }
    public async Task<TenantInfoResponse> GetGrpcBgInfoAsync(Guid tenantId)
    {
        var request = new TenantRequest { TenantId = tenantId.ToString() };
        return await _client.GetInfoAsync(request);
    } 
}
public class WorkspaceService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPublishEndpoint _publisher;
    private readonly IUnitOfWorkService _uow;
    private readonly UserManager<User> _userManager;
    private readonly ITenantProvider _tenantProvider;
    public WorkspaceService( IHttpContextAccessor httpContextAccessor,
         IPublishEndpoint publisher,
         IUnitOfWorkService uow,
        UserManager<User> userManager,
        ITenantProvider tenantProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _publisher = publisher;
        _uow = uow;
        _userManager = userManager;
        _tenantProvider = tenantProvider;
    }

    public async Task Create(CreateWorkspaceRequest payload, CancellationToken token)
    {
        var user = await _userManager.GetUserAsync(_httpContextAccessor.HttpContext?.User);
        if (user == null) throw new UnauthorizedException();
        var createTenant = new UserCreated
        {
            UserId = user.Id,
            TenantName = payload.TenantName ?? user.FullName ?? string.Concat(user.Email?.Split('@')[0] ?? "My", " Workspace"),
        };
        await _publisher.Publish(createTenant, token);
        await _uow.CommitChangesAsync("",token);
    }
}
