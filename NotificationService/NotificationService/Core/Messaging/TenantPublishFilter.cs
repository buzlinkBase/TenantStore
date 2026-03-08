using MassTransit;

namespace OnePunch.Notification.Core.Messaging;
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
public class TenantConsumeFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private readonly ITenantProvider _tenantProvider;
    public TenantConsumeFilter(ITenantProvider tenantProvider) => _tenantProvider = tenantProvider;

    public void Probe(ProbeContext context) => context.CreateFilterScope("tenant-consume-filter");

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (context.Headers.TryGetHeader("X-Tenant-ID", out var value) &&
            Guid.TryParse(value?.ToString(), out var tenantId))
        {
            _tenantProvider.SetTenantId(tenantId);
        }
        await next.Send(context);
    }
}