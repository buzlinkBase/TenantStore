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

/// <summary>Adds the storage blocks an account holds to the plan's base storage: free grants always,
/// paid blocks while their own storage period is paid (or in its grace) -- whatever the plan's status.
/// An unlimited plan stays unlimited; an account without blocks is untouched.</summary>
public sealed class StorageAddOnEntitlementDecorator(
    IEntitlementSource inner,
    IEnumerable<StorageAddOn> addOns,
    StorageSubscription? storage,
    DateTime nowUtc) : IEntitlementSource
{
    public EntitlementSet Resolve()
    {
        var set = inner.Resolve();
        var extraMb = StorageAddOnPricing.CountedMb(addOns, storage, nowUtc);
        if (extraMb <= 0) return set;

        var baseMb = set.Values.TryGetValue(EntitlementKeys.StorageMaxMb, out var current)
            ? EntitlementSnapshotBuilder.ParseLimit(current.Value)
            : 0;
        if (baseMb == EntitlementKeys.Unlimited) return set;

        set.Set(EntitlementKeys.StorageMaxMb, Math.Min(int.MaxValue, baseMb + extraMb).ToString(), EntitlementSources.StorageAddOn);
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
        // Storage blocks belong to the account (one pool for all its companies), so every view of it --
        // a company's, or the account's own -- sees them.
        source = new StorageAddOnEntitlementDecorator(
            source, subscription.StorageAddOns.Where(a => a.DeletedAt == null), subscription.StorageSubscription, nowUtc);
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
            Restriction = RestrictionOf(subscription),
            PunchesAcceptedUntilUtc = readOnlySince is { } since
                ? Earliest(since.AddDays(Math.Max(0, lockedPunchRetentionDays)), subscription.PurgeAfterUtc)
                : null,
        };
    }

    // An operator's suspension outranks a legal hold: it is what the owner can act on. A lapse for
    // non-payment is no restriction -- the owner can still pay their way out of it.
    private static AccessRestriction? RestrictionOf(AccountSubscription subscription)
    {
        if (subscription.SubStatus == AccountSubscriptionStatus.Suspended && subscription.SuspensionReason is { } suspension)
            return new AccessRestriction(AccessRestrictionKinds.Suspended, suspension);
        if (subscription.LegalHold)
            return new AccessRestriction(AccessRestrictionKinds.LegalHold, subscription.LegalHoldReason);
        return null;
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
