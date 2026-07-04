using MassTransit;
using Newtonsoft.Json;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core.Services;
using Serilog;
namespace OnePunch.Auth.Core.Messaging;

public class TenantCreatedWorker : IConsumer<TenantCreatedPayload>
{
    private readonly UserService _userService;
    private readonly IUnitOfWorkService _unitOfWorkService;
    private readonly TenantRequestService _tenantCreationRequest;

    public TenantCreatedWorker(UserService userService,
        IUnitOfWorkService unitOfWorkService,
        TenantRequestService tenantCreationRequest)
    {
        _userService = userService;
        _unitOfWorkService = unitOfWorkService;
        _tenantCreationRequest = tenantCreationRequest;
    }

    public async Task Consume(ConsumeContext<TenantCreatedPayload> context)
    {
        var message = context.Message;
        Log.Logger.Information("Auth:TenantCreatedWorker message {0}",JsonConvert.SerializeObject(message));
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
        user.DefaultTenantRole = "Owner";
        await _userService.UpdateAsync(user);
        await _unitOfWorkService.SaveChangesAsync();
        await _unitOfWorkService.CommitChangesAsync("",context.CancellationToken);
        Log.Logger.Information("TenantCreatedWorker commited");
    }
}
