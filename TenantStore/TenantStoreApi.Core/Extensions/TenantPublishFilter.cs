using BuzlinkRepository;
using MassTransit;

namespace TenantStoreApi.Core.Extensions;

public class TenantPublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    private readonly ITenantProvider _tenantProvider;
    public TenantPublishFilter(ITenantProvider tenantProvider) => _tenantProvider = tenantProvider;
    public void Probe(ProbeContext context) => context.CreateFilterScope("tenant-publish-filter");
    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        context.Headers.Set("X-Tenant-ID", _tenantProvider.TenantId.ToString());
        await next.Send(context);
    }
} 

