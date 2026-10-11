namespace Onepunch.Common.Lib.Subscriptions;

/// <summary>
/// Every feature flag and limit a subscription plan can grant. Services check these keys --
/// never a plan's name -- so plans can be re-cut (or one account given an exception through an
/// override) without touching enforcement code. Mirrored by the frontend's
/// entitlement-keys.const.ts; keep the two in sync.
/// </summary>
public static class EntitlementKeys
{
    // ── Features (value "true"/"false") ─────────────────────────────────────────────────────
    /// <summary>Attendance upload/manual entry, logs, shifts, rotation plans, schedule changes, DTR generation.</summary>
    public const string TimekeepingCore = "timekeeping.core";
    /// <summary>Biometric (ADMS) devices, enrollment and automatic sync of their punches.</summary>
    public const string TimekeepingBiometric = "timekeeping.biometric";
    /// <summary>Mobile Attendance (Geofencing): clocking in and out from the mobile app within geofenced sites.</summary>
    public const string TimekeepingMobile = "timekeeping.mobile";
    /// <summary>The Patrol &amp; Incident Report App.</summary>
    public const string PatrolApp = "patrol.app";
    /// <summary>Leave/OT/OB/Pass Slip/Profile Update applications with a single-step approval.</summary>
    public const string ApplicationsCore = "applications.core";
    /// <summary>Employee self-service portal.</summary>
    public const string PortalSelfService = "portal.self_service";
    /// <summary>Payroll run, payslips, 13th month, last pay, year-end tax adjustment.</summary>
    public const string PayrollCore = "payroll.core";
    /// <summary>Loans and deductions, other income, salary adjustments, cash bond, uniform allowance.</summary>
    public const string PayrollLoansIncome = "payroll.loans_income";
    /// <summary>SSS/PhilHealth/Pag-IBIG/BIR remittances, BIR forms and government upload files.</summary>
    public const string ReportsStatutory = "reports.statutory";
    /// <summary>Approval workflows with more than one step.</summary>
    public const string ApprovalsMultilevel = "approvals.multilevel";
    public const string Analytics = "analytics";
    public const string Announcements = "announcements";
    public const string AuditTrail = "audit.trail";
    /// <summary>Client setup, client billing and the Cost Summary report (agency use).</summary>
    public const string ClientsAgency = "clients.agency";

    // ── Limits (integer value; -1 = unlimited) ──────────────────────────────────────────────
    /// <summary>Companies the account may own. Account-wide.</summary>
    public const string CompaniesMax = "companies.max";
    /// <summary>Active employees for the whole account, shared by every company it owns. Account-wide
    /// like <see cref="CompaniesMax"/>.</summary>
    public const string EmployeesMax = "employees.max";
    /// <summary>Members holding any role beyond the portal-only ones (Employee, Client), excluding the owner.</summary>
    public const string AdminsMax = "admins.max";
    public const string BiometricDevicesMax = "biometric_devices.max";
    /// <summary>File storage for the whole account, in MB: the plan's base capacity plus purchased storage
    /// blocks, shared by every company the account owns. Account-wide like <see cref="CompaniesMax"/>.</summary>
    public const string StorageMaxMb = "storage.max_mb";

    // ── Display-only ────────────────────────────────────────────────────────────────────────
    public const string SupportTier = "support.tier";

    public const int Unlimited = -1;

    public static readonly IReadOnlyList<string> Features =
    [
        TimekeepingCore, TimekeepingBiometric, ApplicationsCore, PortalSelfService, PayrollCore,
        PayrollLoansIncome, ReportsStatutory, ApprovalsMultilevel, Analytics, Announcements,
        AuditTrail, ClientsAgency, TimekeepingMobile, PatrolApp,
    ];

    public static readonly IReadOnlyList<string> Limits =
    [
        CompaniesMax, EmployeesMax, AdminsMax, BiometricDevicesMax, StorageMaxMb,
    ];

    /// <summary>Limits counted across all of an account's companies together, against one account-wide
    /// maximum (so, unlike admins and devices, they can't be overridden for a single company).</summary>
    public static readonly IReadOnlyList<string> PooledLimits = [EmployeesMax, StorageMaxMb];

    public static bool IsPooledLimit(string key) => PooledLimits.Contains(key);

    public static bool IsFeature(string key) => Features.Contains(key);

    public static bool IsLimit(string key) => Limits.Contains(key);

    public static bool IsKnown(string key) => IsFeature(key) || IsLimit(key) || key == SupportTier;
}
