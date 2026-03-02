using MassTransit;
using Serilog;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core;
public class UserConfirmedWorker : IConsumer<TenantUserPayload>
{
    private readonly TenantService _tenantService;

    public UserConfirmedWorker(TenantService tenantService)
    {
        _tenantService = tenantService;
    }
    public async Task Consume(ConsumeContext<TenantUserPayload> context)
    {
        var model = context.Message;
        var tenant =  await _tenantService.FindTenantAsync(model.TenantId, context.CancellationToken);
        if (tenant == null)
        {
            Log.Warning("Tenant {TenantId} not found. Nothing to activate.", model.TenantId);
            return;
        }
        tenant.Status = "Active";
        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }
}