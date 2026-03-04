using BuzlinkRepository;
using Microsoft.AspNetCore.Http;

namespace TenantStoreApi.Core.Providers;

public class WebTenantContextAccessor : ITenantProvider
{
    private Guid _tenantId;
    private readonly IHttpContextAccessor _httpContextAccessor;
    public WebTenantContextAccessor(IHttpContextAccessor httpContextAccessor) 
    {
        _httpContextAccessor = httpContextAccessor;
    }
    public  Guid TenantId
    {
        get
        {
            if (_tenantId != Guid.Empty) return _tenantId;
            var header = _httpContextAccessor.HttpContext?.Request?.Headers["X-Tenant-ID"].FirstOrDefault();
            return Guid.TryParse(header, out var id) ? id : Guid.Empty;
        }
        set => _tenantId = value;
    }
    public  void SetTenantId(Guid tenantId) => _tenantId = tenantId;
}
 