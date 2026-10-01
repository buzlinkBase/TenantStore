namespace Onepunch.Common.Lib.Subscriptions;

public enum SubscriptionAccessMode
{
    /// <summary>Everything the plan's features allow.</summary>
    Full,
    /// <summary>View and export only -- trial ended, suspended for non-payment, or canceled.</summary>
    ReadOnly,
}

/// <summary>Where an entitlement's effective value came from -- shown to support so they can tell
/// a plan limit from a deliberate exception.</summary>
public static class EntitlementSources
{
    public const string Plan = "plan";
    public const string AccountOverride = "account-override";
    public const string CompanyOverride = "company-override";
}

/// <summary>
/// One company's effective entitlements at a moment in time: its owner account's plan, then that
/// account's overrides, then the company's own overrides, then the subscription's access mode.
/// What TenantApi resolves and every other service enforces.
/// </summary>
public sealed record EntitlementSnapshot
{
    /// <summary>Lifecycle status when no subscription exists at all.</summary>
    public const string NoSubscription = "None";

    public Guid TenantId { get; init; }
    public Guid? OwnerUserId { get; init; }
    public string? PlanCode { get; init; }
    public string? PlanName { get; init; }
    /// <summary>Trialing, Active, PastDue, Suspended, Expired, Canceled -- or <see cref="NoSubscription"/>.</summary>
    public string Status { get; init; } = NoSubscription;
    public SubscriptionAccessMode AccessMode { get; init; } = SubscriptionAccessMode.ReadOnly;
    public Dictionary<string, bool> Features { get; init; } = new();
    /// <summary>Limit key → maximum; <see cref="EntitlementKeys.Unlimited"/> for no maximum.</summary>
    public Dictionary<string, int> Limits { get; init; } = new();
    /// <summary>Entitlement key → one of <see cref="EntitlementSources"/>.</summary>
    public Dictionary<string, string> Sources { get; init; } = new();
    public string? SupportTier { get; init; }
    public DateTime? TrialEndsAtUtc { get; init; }
    public DateTime? CurrentPeriodEndUtc { get; init; }
    public DateTime? GraceEndsAtUtc { get; init; }
    public bool CancelAtPeriodEnd { get; init; }
    /// <summary>When the company became read-only (trial end, grace end, cancellation, deletion).</summary>
    public DateTime? ReadOnlySinceUtc { get; init; }
    /// <summary>
    /// Until when biometric devices' punches are still stored while read-only: a retention window
    /// after the lock, cut short once the account is no longer valid at all (data purge date,
    /// company deleted). Null = no cut-off.
    /// </summary>
    public DateTime? PunchesAcceptedUntilUtc { get; init; }

    /// <summary>Status of a company that was deleted -- nothing about it is valid any more.</summary>
    public const string InvalidStatus = "Invalid";

    public bool IsReadOnly => AccessMode == SubscriptionAccessMode.ReadOnly;

    /// <summary>Whether device punches for this company are still stored at <paramref name="nowUtc"/>.</summary>
    public bool AcceptsPunches(DateTime nowUtc) =>
        !IsReadOnly || PunchesAcceptedUntilUtc is not { } until || nowUtc < until;

    public bool HasFeature(string key) => Features.TryGetValue(key, out var enabled) && enabled;

    /// <summary>The maximum for a limit, or null when unlimited. A limit the snapshot doesn't
    /// carry at all counts as 0 -- fail closed.</summary>
    public int? LimitOf(string key) =>
        Limits.TryGetValue(key, out var max) ? (max < 0 ? null : max) : 0;

    /// <summary>Whether <paramref name="used"/> plus <paramref name="adding"/> stays within the limit.</summary>
    public bool Allows(string limitKey, int used, int adding = 1) =>
        LimitOf(limitKey) is not { } max || used + adding <= max;

    /// <summary>Used when enforcement is switched off by configuration: everything allowed.</summary>
    public static EntitlementSnapshot Unrestricted(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Status = "Unrestricted",
        AccessMode = SubscriptionAccessMode.Full,
        Features = EntitlementKeys.Features.ToDictionary(k => k, _ => true),
        Limits = EntitlementKeys.Limits.ToDictionary(k => k, _ => EntitlementKeys.Unlimited),
    };
}
