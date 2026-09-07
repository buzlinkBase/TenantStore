using BuzlinkRepository;

namespace Onepunch.Auth.Api.Tests.TestSupport;

public class FakeTenantProvider : ITenantProvider
{
    public Guid TenantId { get; private set; } = Guid.Empty;

    public void SetTenantId(Guid tenantId) => TenantId = tenantId;
}
