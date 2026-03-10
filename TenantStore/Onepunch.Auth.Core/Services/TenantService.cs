namespace Onepunch.Auth.Core.Services;

public class TenantService
{
    private readonly GetTenantService.GetTenantServiceClient _client;
    private readonly ITenantProvider _tenantProvider;

    public TenantService(GetTenantService.GetTenantServiceClient client,
        ITenantProvider tenantProvider)
    {
        _client = client;
        _tenantProvider = tenantProvider;
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
