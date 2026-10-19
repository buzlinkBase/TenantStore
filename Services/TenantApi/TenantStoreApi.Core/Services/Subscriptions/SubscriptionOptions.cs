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
    /// <summary>How long before a paid period ends the owner is reminded, and may pay for the
    /// next one online.</summary>
    public int RenewalWindowDays { get; set; } = 7;
    /// <summary>How long a locked account's operational data is kept (one year). Payroll, employee and
    /// attendance records are archived rather than destroyed then -- DOLE and BIR retention.</summary>
    public int PurgeAfterDays { get; set; } = 365;
    /// <summary>Off: purge dates are still set and the warning emails still go out, but hrms-api is
    /// never told to delete anything. Deleting is irreversible, so turn it on deliberately.</summary>
    public bool DataPurgeEnabled { get; set; }
    /// <summary>The least warning an account gets before its first purge date: an account that had
    /// already been locked for over a year when purging went live gets this long, not zero.</summary>
    public int MinPurgeNoticeDays { get; set; } = 30;
    /// <summary>On: erasing an account or company from the Platform Console needs a second operator to
    /// approve it. Off only for testing (one operator may approve their own request). A console setting.</summary>
    public bool RequireSecondDeletionApprover { get; set; } = true;
    /// <summary>How long the compliance archive is kept after the company's last payroll / attendance /
    /// accounting entry. DOLE asks 3 years and BIR 5 (NIRC Sec. 235), so one 5-year clock covers both.</summary>
    public int ArchiveRetentionYears { get; set; } = 5;
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
