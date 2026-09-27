using MassTransit;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Services;

namespace OnePunch.Auth.Core.Messaging;

/// <summary>
/// A company was renamed (TenantRenamed, published by Tenant Service after committing its own
/// copies). Updates Auth Service's stored copies of the name, evicts every member's membership
/// cache (it holds the tenant name the company switcher lists), and pushes a "TenantRenamed"
/// signal to each member's live sessions -- the frontend force-refreshes the token and tenant
/// list on it, which picks up the new name without re-logging in.
/// All steps are idempotent, so MassTransit's at-least-once redelivery is harmless.
/// </summary>
public class TenantRenamedWorker : IConsumer<TenantRenamed>
{
    private readonly UserService _userService;
    private readonly MembershipCacheService _membershipCacheService;
    private readonly TenantNotificationService _notificationService;

    public TenantRenamedWorker(UserService userService,
        MembershipCacheService membershipCacheService,
        TenantNotificationService notificationService)
    {
        _userService = userService;
        _membershipCacheService = membershipCacheService;
        _notificationService = notificationService;
    }

    public async Task Consume(ConsumeContext<TenantRenamed> context)
    {
        var message = context.Message;
        if (message == null || message.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(message.TenantName)) return;

        await _userService.ApplyTenantRenameAsync(message.TenantId, message.TenantName, context.CancellationToken);

        foreach (var userId in message.MemberUserIds.Distinct())
        {
            await _membershipCacheService.InvalidateAsync(userId);
            await _notificationService.NotifyTenantRenamed(
                userId, new TenantRenamedNotification { TenantId = message.TenantId, TenantName = message.TenantName });
        }
    }
}
