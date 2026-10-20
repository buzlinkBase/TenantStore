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
    // Storage alerts (SubscriptionEmailTemplates.StorageAlmostFull): how full the account's storage is.
    public int? StoragePercent { get; set; }
    public int? StorageUsedMb { get; set; }
    public int? StorageLimitMb { get; set; }
    // Auto renew: what is renewed ("Payroll plan", "Extra storage"), how, and for how much.
    public string? AutoRenewItem { get; set; }
    /// <summary>"Visa •••• 4242", "Maya". On a renewal-due email it means the renewal is charged automatically.</summary>
    public string? PaymentMethodLabel { get; set; }
    public decimal? Amount { get; set; }
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
    /// <summary>The account's storage reached 95% of its limit.</summary>
    public const string StorageAlmostFull = "storage-almost-full";
    // Storage is billed on its own cycle: PeriodEndUtc / GraceEndsAtUtc are the storage period's.
    /// <summary>The storage renewal window opened: storage is due on PeriodEndUtc.</summary>
    public const string StorageRenewalDue = "storage-renewal-due";
    /// <summary>The storage period ended unpaid: the extra storage still counts until GraceEndsAtUtc.</summary>
    public const string StoragePastDue = "storage-past-due";
    /// <summary>Unpaid past the grace: the extra storage no longer counts.</summary>
    public const string StorageLapsed = "storage-lapsed";
    // Auto renew: AutoRenewItem names what renews, PaymentMethodLabel how.
    public const string AutoRenewOn = "auto-renew-on";
    public const string AutoRenewChargeFailed = "auto-renew-charge-failed";
    public const string AutoRenewStopped = "auto-renew-stopped";
}
