using Microsoft.AspNetCore.SignalR;
using OnePunch.Auth.Core.Hubs;

namespace OnePunch.Auth.Core.Services;

public class TenantNotificationService
{
    private readonly IHubContext<TenantHub, ITenantNotificationClient> _hubContext;

    public TenantNotificationService(IHubContext<TenantHub, ITenantNotificationClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyTenantCreated(Guid userId, TenantCreatedNotification notification) =>
        _hubContext.Clients.User(userId.ToString()).TenantCreated(notification);

    public Task NotifyHrDbCreated(Guid userId, HrDbCreatedNotification notification) =>
        _hubContext.Clients.User(userId.ToString()).HrDbCreated(notification);

    public Task NotifyRolesChanged(Guid userId, RolesChangedNotification notification) =>
        _hubContext.Clients.User(userId.ToString()).RolesChanged(notification);

    public Task NotifySessionRevoked(Guid userId, SessionRevokedNotification notification) =>
        _hubContext.Clients.User(userId.ToString()).SessionRevoked(notification);
}
