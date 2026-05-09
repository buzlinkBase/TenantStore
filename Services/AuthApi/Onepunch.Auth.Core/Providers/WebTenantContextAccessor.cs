using Microsoft.AspNetCore.Http;
namespace OnePunch.Auth.Core.Providers;

public class TenantProviderAccessor : ITenantProvider
{
    private Guid _tenantId;
    private readonly IHttpContextAccessor _httpContextAccessor;
    public TenantProviderAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid TenantId
    {
        get
        {
            if (_tenantId != Guid.Empty) return _tenantId;
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return Guid.Empty;

            // 1. Try to get from Header
            var header = context.Request.Headers["X-Tenant-ID"].FirstOrDefault();
            if (Guid.TryParse(header, out var headerId))
            {
                _tenantId = headerId;
                return _tenantId;
            }

            // 2. Fallback: Try to get from JWT Claims
            // Look for a claim named "tenant-id" (or whatever your claim name is)
            var claim = context.User?.FindFirst("TenantId")?.Value;
            if (Guid.TryParse(claim, out var claimId))
            {
                _tenantId = claimId;
                return _tenantId;
            }
            return Guid.Empty;
        }
    }
    public void SetTenantId(Guid tenantId) => _tenantId = tenantId;
}