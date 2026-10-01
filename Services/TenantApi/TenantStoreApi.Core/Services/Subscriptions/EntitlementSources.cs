using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>Entitlement key → raw value and the layer that set it.</summary>
public sealed class EntitlementSet
{
    private readonly Dictionary<string, (string Value, string Source)> _values = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, (string Value, string Source)> Values => _values;

    public void Set(string key, string value, string source) => _values[key] = (value, source);
}

/// <summary>
/// Decorator pattern: the plan is the base source; each override layer wraps the one below it
/// and replaces whatever keys it covers. Adding a layer (say, a promo bundle) is one more
/// decorator, with no change to the others.
/// </summary>
public interface IEntitlementSource
{
    EntitlementSet Resolve();
}

/// <summary>The plan's own entitlements.</summary>
public sealed class PlanEntitlementSource(Plan? plan) : IEntitlementSource
{
    public EntitlementSet Resolve()
    {
        var set = new EntitlementSet();
        foreach (var entitlement in plan?.Entitlements ?? [])
            set.Set(entitlement.Key, entitlement.Value, EntitlementSources.Plan);
        return set;
    }
}

/// <summary>Applies one layer of overrides (account-wide, or one company's) over the source below.</summary>
public sealed class OverrideEntitlementDecorator(
    IEntitlementSource inner,
    IEnumerable<SubscriptionOverride> overrides,
    string source) : IEntitlementSource
{
    public EntitlementSet Resolve()
    {
        var set = inner.Resolve();
        foreach (var entry in overrides)
            set.Set(entry.Key, entry.Value, source);
        return set;
    }
}

/// <summary>Builds an <see cref="EntitlementSnapshot"/> from a subscription, without touching the database.</summary>
public static class EntitlementSnapshotBuilder
{
    /// <param name="plan">The plan to resolve against -- the subscription's own, or (for a
    /// downgrade check) the one being moved to.</param>
    /// <param name="tenantId">The company; <see cref="Guid.Empty"/> for account-level checks,
    /// which skips company overrides.</param>
    public static EntitlementSnapshot Build(
        AccountSubscription? subscription,
        Plan? plan,
        Guid tenantId,
        DateTime nowUtc,
        SubscriptionStateMachine stateMachine,
        int lockedPunchRetentionDays = 60)
    {
        if (subscription == null)
        {
            return new EntitlementSnapshot { TenantId = tenantId, Status = EntitlementSnapshot.NoSubscription };
        }

        var live = subscription.Overrides
            .Where(o => o.DeletedAt == null && (o.ExpiresAtUtc == null || o.ExpiresAtUtc > nowUtc))
            .ToList();

        IEntitlementSource source = new PlanEntitlementSource(plan);
        source = new OverrideEntitlementDecorator(source, live.Where(o => o.TenantId == null), EntitlementSources.AccountOverride);
        if (tenantId != Guid.Empty)
            source = new OverrideEntitlementDecorator(source, live.Where(o => o.TenantId == tenantId), EntitlementSources.CompanyOverride);

        var values = source.Resolve().Values;
        var accessMode = stateMachine.AccessModeOf(subscription);
        var readOnlySince = accessMode == SubscriptionAccessMode.ReadOnly ? ReadOnlySince(subscription) : null;

        return new EntitlementSnapshot
        {
            TenantId = tenantId,
            OwnerUserId = subscription.OwnerUserId,
            PlanCode = plan?.Code,
            PlanName = plan?.Name,
            Status = subscription.SubStatus.ToString(),
            AccessMode = accessMode,
            Features = EntitlementKeys.Features.ToDictionary(
                key => key,
                key => values.TryGetValue(key, out var v) && ParseBool(v.Value)),
            Limits = EntitlementKeys.Limits.ToDictionary(
                key => key,
                key => values.TryGetValue(key, out var v) ? ParseLimit(v.Value) : 0),
            Sources = values.ToDictionary(kv => kv.Key, kv => kv.Value.Source),
            SupportTier = values.TryGetValue(EntitlementKeys.SupportTier, out var tier) ? tier.Value : null,
            TrialEndsAtUtc = subscription.TrialEndsAtUtc,
            CurrentPeriodEndUtc = subscription.CurrentPeriodEndUtc,
            GraceEndsAtUtc = subscription.GraceEndsAtUtc,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            ReadOnlySinceUtc = readOnlySince,
            PunchesAcceptedUntilUtc = readOnlySince is { } since
                ? Earliest(since.AddDays(Math.Max(0, lockedPunchRetentionDays)), subscription.PurgeAfterUtc)
                : null,
        };
    }

    // The moment the state that made it read-only began.
    private static DateTime? ReadOnlySince(AccountSubscription subscription) => subscription.SubStatus switch
    {
        AccountSubscriptionStatus.Expired => subscription.TrialEndsAtUtc,
        AccountSubscriptionStatus.Suspended => subscription.GraceEndsAtUtc,
        AccountSubscriptionStatus.Canceled => subscription.CanceledAtUtc,
        _ => null,
    };

    // The retention window, cut short by the purge date: past purge the account is no longer valid.
    private static DateTime Earliest(DateTime window, DateTime? purgeAfter) =>
        purgeAfter is { } purge && purge < window ? purge : window;

    public static bool ParseBool(string value) =>
        bool.TryParse(value, out var on) && on;

    // A malformed limit fails closed (0) rather than open.
    public static int ParseLimit(string value) =>
        int.TryParse(value, out var max) && max >= EntitlementKeys.Unlimited ? max : 0;

    /// <summary>Whether <paramref name="value"/> is a valid value for <paramref name="key"/>.</summary>
    public static bool IsValidValue(string key, string value) =>
        EntitlementKeys.IsFeature(key) ? bool.TryParse(value, out _)
        : EntitlementKeys.IsLimit(key) ? int.TryParse(value, out var max) && max >= EntitlementKeys.Unlimited
        : key == EntitlementKeys.SupportTier && !string.IsNullOrWhiteSpace(value);
}
