namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>"Subscriptions" configuration section.</summary>
public sealed class SubscriptionOptions
{
    public const string Section = "Subscriptions";

    /// <summary>Off: every allowance check passes (deploy dark before plans are announced).
    /// Lifecycle, reminders and the billing screens keep working either way.</summary>
    public bool EnforcementEnabled { get; set; } = true;

    public int TrialDays { get; set; } = 7;
    /// <summary>The plan a trial runs on -- full Premium.</summary>
    public string TrialPlanCode { get; set; } = "premium";
    /// <summary>Full access after a period ends unpaid, before suspension.</summary>
    public int GraceDays { get; set; } = 7;
    /// <summary>How long a canceled account's data is kept.</summary>
    public int PurgeAfterDays { get; set; } = 90;
    /// <summary>
    /// How long biometric punches are still stored after a company becomes read-only (Expired,
    /// Suspended, Canceled) -- never past the account's data purge date. Platform-wide for now;
    /// the Platform Admin Console will own it later, with this as the fallback.
    /// </summary>
    public int LockedPunchRetentionDays { get; set; } = 60;
    /// <summary>A platform admin may extend a trial once, by at most this many days.</summary>
    public int MaxTrialExtensionDays { get; set; } = 30;

    /// <summary>
    /// Accounts that already own a company when subscriptions go live are given this plan,
    /// active until <see cref="LegacyPeriodEndUtc"/>, so live customers aren't locked out.
    /// </summary>
    public string LegacyPlanCode { get; set; } = "premium";
    /// <summary>Unset: 90 days from the backfill. Set the real date before go-live.</summary>
    public DateTime? LegacyPeriodEndUtc { get; set; }

    /// <summary>When the lifecycle job fires (Quartz cron, UTC). Default: the top of every hour.</summary>
    public string LifecycleCron { get; set; } = "0 0 * * * ?";

    /// <summary>Off: Quartz isn't started, so TenantApi can run before the QRTZ_ tables exist.
    /// Statuses still follow the dates on every read; only emails and saved transitions wait.</summary>
    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>Shown after an owner picks a plan, until online payment exists (phase 2): where to
    /// send the bank transfer and what reference to use.</summary>
    public string PaymentInstructions { get; set; } =
        "Pay by bank transfer and email the receipt to billing@onepunch.site with your account email as the reference. Your plan is activated once the payment is confirmed.";

    /// <summary>Repeat trials from these domains aren't flagged -- anyone can have an address there.</summary>
    public List<string> FreeEmailDomains { get; set; } =
    [
        "gmail.com", "yahoo.com", "yahoo.com.ph", "outlook.com", "hotmail.com", "live.com",
        "icloud.com", "me.com", "proton.me", "protonmail.com", "aol.com", "ymail.com", "mail.com",
    ];
}

/// <summary>A subscription rule refused the request (bad state, unknown plan, ...). 400.</summary>
public class SubscriptionRuleException(string message) : InvalidOperationException(message);

/// <summary>One limit the account would exceed.</summary>
public sealed record LimitViolation(string LimitKey, Guid? TenantId, string? CompanyName, int Used, int? Max);

/// <summary>Usage is over a plan's limits (a downgrade the account doesn't fit yet). 409.</summary>
public sealed class PlanLimitExceededException(IReadOnlyList<LimitViolation> violations)
    : SubscriptionRuleException(Describe(violations))
{
    public IReadOnlyList<LimitViolation> Violations { get; } = violations;

    private static string Describe(IReadOnlyList<LimitViolation> violations) =>
        "Your usage is over this plan's limits: " + string.Join("; ", violations.Select(v =>
            $"{(v.CompanyName != null ? v.CompanyName + " " : "")}{v.LimitKey} {v.Used}" +
            (v.Max is { } max ? $" (max {max})" : " (couldn't be checked)"))) + ".";
}
