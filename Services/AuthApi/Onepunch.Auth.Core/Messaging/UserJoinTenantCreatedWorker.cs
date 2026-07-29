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
        var user = await _userService.GetByIdAsync(context.Message.UserId.ToString());
        if (user == null) return;

        // Only set the default tenant if the user doesn't have one yet
        if (user.DefaultTenantId == null || user.DefaultTenantId == Guid.Empty)
        {
            user.DefaultTenantId = context.Message.TenantId;
            user.DefaultTenantName = context.Message.TenantName;
            user.DefaultTenantRoles = context.Message.Roles;
            await _userService.UpdateAsync(user);
            await _userService.CommitChangesAsync();
        }
    }
}
