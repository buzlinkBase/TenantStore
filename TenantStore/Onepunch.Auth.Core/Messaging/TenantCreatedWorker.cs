using MassTransit;
using OnePunch.Auth.Core.Services;
namespace OnePunch.Auth.Core.Messaging;

public class TenantCreatedWorker : IConsumer<TenantCreatedPayload>
{
    private readonly UserService _userService;
    public TenantCreatedWorker(UserService userService)
    {
        _userService = userService;
    }

    public async Task Consume(ConsumeContext<TenantCreatedPayload> context)
    {
        var user = await _userService.GetByIdAsync(context.Message.UserId.ToString());
        if (user == null) return;
        user.DefaultTenantId = context.Message.TenantId;
        await _userService.UpdateAsync(user);
        _userService.CommitChanges();
    }
}
