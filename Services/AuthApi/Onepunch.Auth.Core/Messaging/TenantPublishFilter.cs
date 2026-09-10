using MassTransit;

namespace OnePunch.Auth.Core.Messaging;

public class TenantPublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    private readonly ITenantProvider _tenantProvider;
    public TenantPublishFilter(ITenantProvider tenantProvider) => _tenantProvider = tenantProvider;
    public void Probe(ProbeContext context) => context.CreateFilterScope("hrms-publish-filter");
    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        // Only fall back to the caller's ambient tenant if the publish site hasn't already set an
        // explicit one -- some flows (e.g. accepting an invitation to a different/new tenant than
        // whatever the caller's current JWT reflects) publish messages ABOUT a tenant other than
        // the caller's own ambient one, and must not have that overwritten here.
        if (!context.Headers.TryGetHeader("X-Tenant-ID", out _))
        {
            context.Headers.Set("X-Tenant-ID", _tenantProvider.TenantId.ToString());
        }
        await next.Send(context);
    }
}

public class TenantConsumeFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private readonly ITenantProvider _tenantProvider;
    public TenantConsumeFilter(ITenantProvider tenantProvider) => _tenantProvider = tenantProvider;

    public void Probe(ProbeContext context) => context.CreateFilterScope("hrms-consume-filter");

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