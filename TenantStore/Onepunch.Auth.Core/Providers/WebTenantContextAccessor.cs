using Microsoft.AspNetCore.Http;
namespace OnePunch.Auth.Core.Providers;

public class WebTenantContextAccessor : ITenantProvider
{
    private Guid _tenantId;
    private readonly IHttpContextAccessor _httpContextAccessor;
    public WebTenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    public Guid TenantId
    {
        get
        {
            if (_tenantId != Guid.Empty) return _tenantId;
            var header = _httpContextAccessor.HttpContext?.Request?.Headers["X-Tenant-ID"].FirstOrDefault();
            return Guid.TryParse(header, out var id) ? id : Guid.Empty;
        }
        set => _tenantId = value;
    }
    public void SetTenantId(Guid tenantId) => _tenantId = tenantId;
}

//public class MessagingTenantContextAccessor : ITenantContextAccessor
//{
//    public MessagingTenantContextAccessor(ITenantProvider tenantProvider)
//    {
//        _tenantProvider = tenantProvider;
//    }
//    private Guid _currentTenantId;
//    private readonly ITenantProvider _tenantProvider;
//    public Guid GetTenantId() => _currentTenantId;
//    public void SetTenantId(Guid tenantId)
//    {
//        _currentTenantId = tenantId;
//        _tenantProvider.SetTenantId(tenantId);
//    }
//}
