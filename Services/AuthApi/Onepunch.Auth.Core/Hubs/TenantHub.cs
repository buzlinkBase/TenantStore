using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace OnePunch.Auth.Core.Hubs;

public interface ITenantNotificationClient
{
    Task TenantCreated(TenantCreatedNotification notification);
    Task HrDbCreated(HrDbCreatedNotification notification);
    Task RolesChanged(RolesChangedNotification notification);
    Task SessionRevoked(SessionRevokedNotification notification);
    Task TenantRenamed(TenantRenamedNotification notification);
    Task SignedOut(SignedOutNotification notification);
}

/// <summary>
/// The account's password was changed or reset, and its other sessions were revoked -- every
/// open app signs out now instead of when its access token expires. The one that made the change
/// keeps going: it's the session whose access token carries <see cref="ExceptTokenId"/> (its
/// "jti"). Null after a reset, when no session is kept.
/// </summary>
public class SignedOutNotification
{
    public string? ExceptTokenId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public static class SignedOutReasons
{
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
}

/// <summary>A company the user belongs to was renamed -- the frontend refreshes its session
/// (token tenantName claim + company switcher) to pick up the new name.</summary>
public class TenantRenamedNotification
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
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

public class RolesChangedNotification
{
    public Guid TenantId { get; set; }
}

public class SessionRevokedNotification
{
    public Guid TenantId { get; set; }
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
