using BuzlinkRepository;
using Microsoft.AspNetCore.Http;
using TenantStoreApi.Core.Extensions;

namespace TenantStoreApi.Core.Providers;

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
            // If already cached/set, return it immediately
            if (_tenantId != Guid.Empty)
                return _tenantId;

            var context = _httpContextAccessor.HttpContext;
            if (context == null)
                return Guid.Empty;

            // 1. Try to get from User JWT Claims first (Now case-insensitive thanks to your extension!)
            var claimTenantId = context.User?.GetUserClaim("tenantId");
            if (Guid.TryParse(claimTenantId, out var claimId) && claimId != Guid.Empty)
            {
                _tenantId = claimId;
                return _tenantId;
            }

            // 2. Fallback: Try to get from Header ("X-Tenant-ID") last
            // Cleaned up using your new 'GetHeader' extension method!
            var header = context.Request.GetHeader("X-Tenant-ID");
            if (Guid.TryParse(header, out var headerId) && headerId != Guid.Empty)
            {
                _tenantId = headerId;
                return _tenantId;
            }

            return Guid.Empty;
        }
    }

    public void SetTenantId(Guid tenantId) => _tenantId = tenantId;
}