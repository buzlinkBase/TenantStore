using Microsoft.AspNetCore.Http;

namespace OnePunch.Auth.Core.Providers;
public interface ITenantContextAccessor
{
    void SetTenantId(Guid tenantId);
    Guid GetTenantId();
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
    public void SetTenantId(Guid tenantId)
    {
        _tenantProvider.SetTenantId(tenantId);
    }
}
public class MessagingTenantContextAccessor : ITenantContextAccessor
{
    public MessagingTenantContextAccessor(ITenantProvider tenantProvider)
    {
        _tenantProvider = tenantProvider;
    }
    private Guid _currentTenantId;
    private readonly ITenantProvider _tenantProvider;
    public Guid GetTenantId() => _currentTenantId;
    public void SetTenantId(Guid tenantId)
    {
        _currentTenantId = tenantId;
        _tenantProvider.SetTenantId(tenantId);  
    }
}
