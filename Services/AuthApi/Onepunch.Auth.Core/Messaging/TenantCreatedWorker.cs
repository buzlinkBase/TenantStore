using MassTransit;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core.Services;
using Serilog;
namespace OnePunch.Auth.Core.Messaging;

public class TenantCreatedWorker : IConsumer<TenantCreatedPayload>
{
    private readonly UserService _userService;
    private readonly TenantRequestService _tenantCreationRequest;

    public TenantCreatedWorker(UserService userService,
        TenantRequestService tenantCreationRequest)
    {
        _userService = userService;
        _tenantCreationRequest = tenantCreationRequest;
    }

    public async Task Consume(ConsumeContext<TenantCreatedPayload> context)
    {

        var message = context.Message;
        var user = await _userService.GetByIdAsync(context.Message.UserId.ToString());
        if (user == null)
        {
            Log.Logger.Error("TenantCreatedWorker user not found:{0}", message.UserId);
            return;
        }

        var request = await _tenantCreationRequest.FindOne(message.TenantId);
        if (request != null)
        {
            request.Status = TenantCreationStatus.Created;
            _userService.Context.TenantCreationRequests.Update(request);
        }

        user.DefaultTenantId = context.Message.TenantId;
        user.DefaultTenantName = context.Message.TenantName;
        user.DefaultTenantRole = "Owner";
        await _userService.UpdateAsync(user);
        await _userService.CommitChangesAsync();
    } 
}
