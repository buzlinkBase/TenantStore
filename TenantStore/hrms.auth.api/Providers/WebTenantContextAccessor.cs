namespace OnePunch.Auth.Api.Providers;

public interface ITenantContextAccessor
{
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
} 