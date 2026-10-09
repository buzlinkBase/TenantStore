namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// Published by Tenant Service whenever an account's effective entitlements may have changed --
/// a lifecycle transition, plan change, payment, or override. Services caching entitlements per
/// company evict <see cref="TenantIds"/>; Auth Service tells <see cref="MemberUserIds"/>' live
/// sessions to refresh so locked/unlocked features show without re-logging in.
/// </summary>
public record SubscriptionChanged
{
    public Guid OwnerUserId { get; set; }
    public List<Guid> TenantIds { get; set; } = new();
    public List<Guid> MemberUserIds { get; set; } = new();
    /// <summary>The billing event that caused it, e.g. "trial.expired", "payment.recorded".</summary>
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Published by Tenant Service for each subscription email (trial reminders, past-due, suspended,
/// purge warnings); Notification Service renders <see cref="Template"/> and sends it. Dates are
/// UTC -- the template shows them in Asia/Manila.
/// </summary>
public record SubscriptionReminderEmail
{
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    /// <summary>One of <see cref="SubscriptionEmailTemplates"/>.</summary>
    public string Template { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public DateTime? TrialEndsAtUtc { get; set; }
    public DateTime? PeriodEndUtc { get; set; }
    public DateTime? GraceEndsAtUtc { get; set; }
    public DateTime? PurgeAfterUtc { get; set; }
    public int? EmployeeCount { get; set; }
    public int? CompanyCount { get; set; }
    // Storage alerts (SubscriptionEmailTemplates.StorageAlmostFull): which company, and how full.
    public string? CompanyName { get; set; }
    public int? StoragePercent { get; set; }
    public int? StorageUsedMb { get; set; }
    public int? StorageLimitMb { get; set; }
}

public static class SubscriptionEmailTemplates
{
    public const string TrialWelcome = "trial-welcome";
    public const string TrialUsageSummary = "trial-usage-summary";
    public const string TrialEndsTomorrow = "trial-ends-tomorrow";
    public const string TrialEnded = "trial-ended";
    public const string TrialExpiredReminder = "trial-expired-reminder";
    public const string PaymentDue = "payment-due";
    public const string PastDue = "past-due";
    public const string Suspended = "suspended";
    public const string Canceled = "canceled";
    public const string PurgeWarning = "purge-warning";
    /// <summary>A company's storage reached 95% of its limit.</summary>
    public const string StorageAlmostFull = "storage-almost-full";
}
