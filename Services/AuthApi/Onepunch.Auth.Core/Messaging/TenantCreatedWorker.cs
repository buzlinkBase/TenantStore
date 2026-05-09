using MassTransit;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Core.Services;
namespace OnePunch.Auth.Core.Messaging;

public class TenantCreatedWorker : IConsumer<TenantCreatedPayload>
{
    private readonly UserService _userService;
    private readonly TenantCreationRequestStatusService _tenantCreationRequest;

    public TenantCreatedWorker(UserService userService,
        TenantCreationRequestStatusService tenantCreationRequest)
    {
        _userService = userService;
        _tenantCreationRequest = tenantCreationRequest;
    }

    public async Task Consume(ConsumeContext<TenantCreatedPayload> context)
    {
        var message = context.Message;
        var user = await _userService.GetByIdAsync(context.Message.UserId.ToString());
        if (user == null) return;
        var request = await _tenantCreationRequest.FindOne(message.TenantId);
        if (request != null)
        {
            request.Status = TenantCreationStatus.Created;
            _userService.Context.TenantCreationRequests.Update(request);
        }
        user.DefaultTenantId = context.Message.TenantId;
        user.DefaultTenantName = context.Message.TenantName;
        await _userService.UpdateAsync(user);
        _userService.CommitChanges();
    } 
}
