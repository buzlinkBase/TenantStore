using MassTransit;
using Onepunch.Common.Lib.DTO;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Services;
using Serilog;

namespace OnePunch.Auth.Core.Messaging;

public class HrDbCreatedWorker : IConsumer<HrisOrgProvisionedPayload>
{
    private readonly TenantRequestService _tenantRequestService;
    private readonly TenantNotificationService _tenantNotificationService;
    private readonly IUnitOfWorkService _unitOfWorkService;

    public HrDbCreatedWorker(TenantRequestService tenantRequestService,
        TenantNotificationService tenantNotificationService,
        IUnitOfWorkService unitOfWorkService)
    {
        _tenantRequestService = tenantRequestService;
        _tenantNotificationService = tenantNotificationService;
        _unitOfWorkService = unitOfWorkService;
    }

    public async Task Consume(ConsumeContext<HrisOrgProvisionedPayload> context)
    {
        var message = context.Message;

        var request = await _tenantRequestService.FindByTenant(message.TenantId);
        if (request == null)
        {
            Log.Logger.Error("HrDbCreatedWorker unable to locate tenant request:{0}", message.TenantId);
            return;
        }

        var failed = message.Status.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || message.Status.Contains("error", StringComparison.OrdinalIgnoreCase);

        request.HrDbStatus = message.Status;
        request.HrDbReady = !failed;
        _unitOfWorkService.Context.TenantCreationRequests.Update(request);
        await _unitOfWorkService.SaveChangesAsync();

        await _tenantNotificationService.NotifyHrDbCreated(request.UserId, new HrDbCreatedNotification
        {
            TenantId = message.TenantId,
            DatabaseName = message.DatabaseName,
            Status = message.Status
        });
    }
}
