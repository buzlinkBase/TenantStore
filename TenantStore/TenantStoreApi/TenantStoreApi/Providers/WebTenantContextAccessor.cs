using BuzlinkRepository;

namespace TenantStoreApi.Providers;
public interface ITenantContextAccessor
{
    Guid GetTenantId();
}
public interface IAppConfigurationProvider
{
    string? GetConnectionString(string name);
}


public class WebTenantContextAccessor : ITenantContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantProvider _tenantProvider;

    public WebTenantContextAccessor(IHttpContextAccessor httpContextAccessor,
        ITenantProvider tenantProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantProvider = tenantProvider;
    }
    public Guid GetTenantId()
    {
        var header = _httpContextAccessor.HttpContext?.Request?.Headers["X-Tenant-ID"].FirstOrDefault();
        var tenantId = Guid.TryParse(header, out var id) ? id : Guid.Empty;
        _tenantProvider.SetTenantId(tenantId);
        return tenantId;
    }
}

public class WebAppConfigurationProvider : IAppConfigurationProvider
{
    private readonly IConfiguration _configuration;

    public WebAppConfigurationProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    public string? GetConnectionString(string name)
    {
        return _configuration.GetConnectionString(name);
    }
}

