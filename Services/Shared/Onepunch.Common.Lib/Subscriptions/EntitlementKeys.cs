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
    /// <summary>Biometric (ADMS) devices and enrollment.</summary>
    public const string TimekeepingBiometric = "timekeeping.biometric";
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
    /// <summary>Companies the account may own. Account-wide; every other limit is per company.</summary>
    public const string CompaniesMax = "companies.max";
    public const string EmployeesMax = "employees.max";
    /// <summary>Members holding any role beyond the portal-only ones (Employee, Client), excluding the owner.</summary>
    public const string AdminsMax = "admins.max";
    public const string BiometricDevicesMax = "biometric_devices.max";

    // ── Display-only ────────────────────────────────────────────────────────────────────────
    public const string SupportTier = "support.tier";

    public const int Unlimited = -1;

    public static readonly IReadOnlyList<string> Features =
    [
        TimekeepingCore, TimekeepingBiometric, ApplicationsCore, PortalSelfService, PayrollCore,
        PayrollLoansIncome, ReportsStatutory, ApprovalsMultilevel, Analytics, Announcements,
        AuditTrail, ClientsAgency,
    ];

    public static readonly IReadOnlyList<string> Limits =
    [
        CompaniesMax, EmployeesMax, AdminsMax, BiometricDevicesMax,
    ];

    public static bool IsFeature(string key) => Features.Contains(key);

    public static bool IsLimit(string key) => Limits.Contains(key);

    public static bool IsKnown(string key) => IsFeature(key) || IsLimit(key) || key == SupportTier;
}
