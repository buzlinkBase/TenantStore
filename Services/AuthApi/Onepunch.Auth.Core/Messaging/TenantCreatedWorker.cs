using MassTransit;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Services;
using Serilog;
namespace OnePunch.Auth.Core.Messaging;

public class TenantCreatedWorker : IConsumer<TenantCreationCompleted>
{
    private readonly UserService _userService;
    private readonly IUnitOfWorkService _unitOfWorkService;
    private readonly TenantRequestService _tenantCreationRequest;
    private readonly TenantNotificationService _tenantNotificationService;

    public TenantCreatedWorker(UserService userService,
        IUnitOfWorkService unitOfWorkService,
        TenantRequestService tenantCreationRequest,
        TenantNotificationService tenantNotificationService)
    {
        _userService = userService;
        _unitOfWorkService = unitOfWorkService;
        _tenantCreationRequest = tenantCreationRequest;
        _tenantNotificationService = tenantNotificationService;
    }

    public async Task Consume(ConsumeContext<TenantCreationCompleted> context)
    {
        try
        {
            var message = context.Message;
            Log.Logger.Information("Auth:TenantCreatedWorker received for activation");
            var user = await _userService.GetByIdAsync(context.Message.UserId.ToString());
            if (user == null)
            {
                Log.Logger.Error("TenantCreatedWorker user not found:{0}", message.UserId);
                return;
            }
            var request = await _tenantCreationRequest.FindByTenant(message.TenantId);
            if (request != null)
            {
                request.Status = TenantCreationStatus.Created;
                request.TenantName = message.TenantName;
                request.RequestExpiry = null;
                _unitOfWorkService.Context.TenantCreationRequests.Update(request);
                await _unitOfWorkService.SaveChangesAsync();
            }
            else
            {
                Log.Logger.Error("unable to locate tenant in Auth::tenantCreationRequest", message.UserId);
                return;
            }
            user.DefaultTenantId = context.Message.TenantId;
            user.DefaultTenantName = context.Message.TenantName;
            user.DefaultTenantRoles = message.Roles;
            await _userService.UpdateAsync(user);
            await _unitOfWorkService.SaveChangesAsync();
            await _unitOfWorkService.CommitChangesAsync("", context.CancellationToken);
            Log.Logger.Information("TenantCreatedWorker commited");

            await _tenantNotificationService.NotifyTenantCreated(message.UserId, new TenantCreatedNotification
            {
                TenantId = message.TenantId,
                TenantName = message.TenantName ?? "",
                Roles = message.Roles
            });
        }
        catch (Exception ex)
        {
            Log.Logger.Error("TenantCreatedWorker user not found:{0}", ex.Message);
            throw;
        }
    }
}
