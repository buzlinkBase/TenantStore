using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace OnePunch.Auth.Core.Hubs;

public interface ITenantNotificationClient
{
    Task TenantCreated(TenantCreatedNotification notification);
    Task HrDbCreated(HrDbCreatedNotification notification);
}

public class TenantCreatedNotification
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}

public class HrDbCreatedNotification
{
    public Guid TenantId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

[Authorize]
public class TenantHub : Hub<ITenantNotificationClient>
{
}

public class TenantHubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst("sub")?.Value
            ?? connection.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }
}
