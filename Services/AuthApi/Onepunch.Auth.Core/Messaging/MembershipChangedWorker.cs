using MassTransit;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Services;

namespace OnePunch.Auth.Core.Messaging;

/// <summary>
/// Flow H (Cache Invalidation): evicts the affected user's membership cache entry whenever
/// Tenant Service reports a role/membership change, rather than waiting for the 5-min TTL.
/// Redis DEL is naturally idempotent, so MassTransit's default at-least-once retry (throw on
/// failure -> redelivery -> harmless re-delete) is sufficient without an inbox/dedup table.
/// </summary>
public class MembershipChangedWorker : IConsumer<MembershipChanged>
{
    private readonly MembershipCacheService _membershipCacheService;
    private readonly UserService _userService;
    private readonly TenantNotificationService _notificationService;

    public MembershipChangedWorker(MembershipCacheService membershipCacheService,
        UserService userService,
        TenantNotificationService notificationService)
    {
        _membershipCacheService = membershipCacheService;
        _userService = userService;
        _notificationService = notificationService;
    }

    public async Task Consume(ConsumeContext<MembershipChanged> context)
    {
        if (context.Message == null) return;

        // Invalidate on every change type, matching this class's own doc comment -- the previous
        // early return here (on empty NewStatus) meant a RoleChanged event did nothing at all on
        // the AuthApi side, not even this.

        await _membershipCacheService.InvalidateAsync(context.Message.UserId);

        // Syncs the default-tenant fallback cache for role changes and clears it when the
        // user's current default tenant is revoked.
        await _userService.ApplyMembershipChangeAsync(
            context.Message.UserId,
            context.Message.TenantId,
            context.Message.NewStatus,
            context.Message.ChangeType == "RoleChanged",
            context.Message.NewRoles,
            context.CancellationToken);

        var reactivated = context.Message.ChangeType == "StatusChanged" &&
                          string.Equals(context.Message.NewStatus, "Active", StringComparison.OrdinalIgnoreCase);

        // Pushes a live "your roles changed" signal to the affected user's frontend session(s)
        // (if any are connected) so they refresh their auth session and cache instead of waiting
        // for the JWT to naturally expire. A no-op for a user with no live hub connection --
        // they still fall back to the existing expiry-based refresh.
        //
        // Reused for reactivation (StatusChanged back to Active) too, not just RoleChanged: the
        // handler's refresh pulls this account's full current tenant list from the server (the
        // cache invalidation above guarantees that read is fresh, not the stale pre-revoke
        // snapshot), which naturally restores a tenant to the frontend's tenant switcher the
        // moment it's reactivated -- the same round trip a role change already needed, just
        // triggered by a different kind of membership change.
        if (context.Message.ChangeType == "RoleChanged" || reactivated)
        {
            await _notificationService.NotifyRolesChanged(
                context.Message.UserId,
                new RolesChangedNotification { TenantId = context.Message.TenantId });
        }
        // A member moved to any non-Active status (Revoked, Inactive, ...) still has an access
        // token that's cryptographically valid for up to its remaining lifetime -- silently
        // refreshing (like RoleChanged above) isn't enough, since none of these statuses should
        // leave the user signed in. Push an immediate forced logout instead of waiting for that
        // token to expire on its own.
        else if (context.Message.ChangeType == "StatusChanged" &&
                 !string.IsNullOrWhiteSpace(context.Message.NewStatus))
        {
            await _notificationService.NotifySessionRevoked(
                context.Message.UserId,
                new SessionRevokedNotification { TenantId = context.Message.TenantId });
        }
    }
}
