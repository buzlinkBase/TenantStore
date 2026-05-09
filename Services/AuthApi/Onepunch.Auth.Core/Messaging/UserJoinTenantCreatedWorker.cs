using MassTransit;
using OnePunch.Auth.Core.Services;
namespace OnePunch.Auth.Core.Messaging;

public class UserJoinTenantCreatedWorker : IConsumer<UserJoinToTenantPayload>
{
    private readonly UserService _userService;
    public UserJoinTenantCreatedWorker(UserService userService)
    {
        _userService = userService;
    }

    public async Task Consume(ConsumeContext<UserJoinToTenantPayload> context)
    {
        //TODO set latest as default
        //capture to next login 
        //or via signal
        var user = await _userService.GetByIdAsync(context.Message.UserId.ToString());
        if (user == null) return;
        user.DefaultTenantId = context.Message.TenantId;
        user.DefaultTenantName = context.Message.TenantName;
        await _userService.UpdateAsync(user);
        _userService.CommitChanges();
    }
}
