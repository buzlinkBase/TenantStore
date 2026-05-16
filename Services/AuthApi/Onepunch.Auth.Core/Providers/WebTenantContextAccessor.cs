using Microsoft.AspNetCore.Http;
using System.Security.Claims;
namespace OnePunch.Auth.Core.Providers;

public class TenantProviderAccessor : ITenantProvider
{
    private Guid _tenantId=Guid.Empty;
    private readonly IHttpContextAccessor _httpContextAccessor;
    public TenantProviderAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    public Guid TenantId
    {
        get
        {
            //if (_tenantId != Guid.Empty) return _tenantId;
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return Guid.Empty;

            // 1. Try to get from Header
            if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var headerValues))
            {
                if (Guid.TryParse(headerValues.FirstOrDefault(), out var headerId))
                {
                    _tenantId = headerId;
                    return _tenantId;
                }
            }

            // 2. Fallback: Try to get from JWT Claims
            var claimValue = context.User?.FindFirstValue("TenantId");
            if (Guid.TryParse(claimValue, out var claimId))
            {
                _tenantId = claimId;
                return _tenantId;
            }

            return Guid.Empty;
        }
    }
    public void SetTenantId(Guid tenantId) => _tenantId = tenantId;
}