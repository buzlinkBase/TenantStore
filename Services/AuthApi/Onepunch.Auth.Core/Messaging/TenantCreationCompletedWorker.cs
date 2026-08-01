using MassTransit;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Services;
using Serilog;
namespace OnePunch.Auth.Core.Messaging;

public class TenantCreationCompletedWorker : IConsumer<TenantCreationCompleted>
{
    private readonly UserService _userService;
    private readonly IUnitOfWorkService _uow;
    private readonly TenantRequestService _tenantCreationRequest;
    private readonly TenantNotificationService _tenantNotificationService;

    public TenantCreationCompletedWorker(UserService userService,
        IUnitOfWorkService uow,
        TenantRequestService tenantCreationRequest,
        TenantNotificationService tenantNotificationService)
    {
        _userService = userService;
        _uow = uow;
        _tenantCreationRequest = tenantCreationRequest;
        _tenantNotificationService = tenantNotificationService;
    }

    public async Task Consume(ConsumeContext<TenantCreationCompleted> context)
    {
        try
        {
            var message = context.Message;
            var user = await _userService.GetByIdAsync(context.Message.UserId.ToString());
            if (user == null)
            {
                Log.Logger.Error("TenantCreationCompletedWorker user not found:{0}", message.UserId);
                return;
            }
            var request = await _tenantCreationRequest.FindByTenant(message.TenantId);
            if (request != null)
            {
                request.Status = TenantCreationStatus.Created;
                request.TenantName = message.TenantName;
                request.RequestExpiry = null;

                _uow.Context.TenantCreationRequests.Update(request);
                await _uow.SaveChangesAsync();
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
            await _uow.SaveChangesAsync();
            await _userService.CommitChangesAsync(context.CancellationToken);

            await _tenantNotificationService.NotifyTenantCreated(message.UserId, new TenantCreatedNotification
            {
                TenantId = message.TenantId,
                TenantName = message.TenantName ?? "",
                Roles = message.Roles
            }); 
        }
        catch (Exception ex)
        {
            Log.Logger.Error($"{nameof(TenantCreationCompletedWorker)} err:{0}", ex.Message);
            throw;
        }
    }
}
