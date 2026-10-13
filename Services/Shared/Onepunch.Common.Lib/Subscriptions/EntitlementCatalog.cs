using Onepunch.Common.Lib.Security;

namespace Onepunch.Common.Lib.Subscriptions;

public enum EntitlementKind
{
    /// <summary>On/off ("true"/"false").</summary>
    Feature,
    /// <summary>A whole number; -1 means unlimited.</summary>
    Limit,
    /// <summary>Shown to the customer, never enforced (e.g. the support tier).</summary>
    Display,
}

/// <summary>
/// One entitlement as the platform console shows it: what it is called and what it controls.
/// <paramref name="Product"/> is the system it belongs to (a <see cref="PermissionProducts"/> value), or
/// <see cref="EntitlementCatalog.AccountWide"/> for what spans every system; the console groups by it.
/// </summary>
public sealed record EntitlementInfo(string Key, EntitlementKind Kind, string Label, string Description, string Product = PermissionProducts.Hrms);

/// <summary>
/// The human-readable side of <see cref="EntitlementKeys"/>, so the console can render a plan's
/// entitlements without hardcoding their names. A test keeps the two in step: every key has an entry.
/// </summary>
public static class EntitlementCatalog
{
    /// <summary>The group for entitlements that belong to the account rather than to one system.</summary>
    public const string AccountWide = "account";

    public static readonly IReadOnlyList<EntitlementInfo> All =
    [
        new(EntitlementKeys.HrmsCore, EntitlementKind.Feature, "HRMS system",
            "The HRMS system: timekeeping, payroll, applications and the employee portal. Included in every standard plan."),
        new(EntitlementKeys.TimekeepingCore, EntitlementKind.Feature, "Timekeeping",
            "Attendance upload and manual entry, logs, shifts, rotation plans, schedule changes, DTR generation."),
        new(EntitlementKeys.TimekeepingBiometric, EntitlementKind.Feature, "Biometric auto sync",
            "Biometric (ADMS) devices, enrollment and automatic sync of their punches."),
        new(EntitlementKeys.TimekeepingMobile, EntitlementKind.Feature, "Mobile Attendance (Geofencing)",
            "Clock in and out from the mobile app, restricted to geofenced work sites."),
        new(EntitlementKeys.PatrolApp, EntitlementKind.Feature, "Patrol & Incident Report App", "Guard patrols and incident reporting from the mobile app."),
        new(EntitlementKeys.ApplicationsCore, EntitlementKind.Feature, "Applications",
            "Leave, OT, OB, pass slip and profile-update applications with a single-step approval."),
        new(EntitlementKeys.PortalSelfService, EntitlementKind.Feature, "Employee portal",
            "Employee self-service portal."),
        new(EntitlementKeys.PayrollCore, EntitlementKind.Feature, "Payroll",
            "Payroll run, payslips, 13th month, last pay and year-end tax adjustment."),
        new(EntitlementKeys.PayrollLoansIncome, EntitlementKind.Feature, "Loans & other income",
            "Loans and deductions, other income, salary adjustments, cash bond and uniform allowance."),
        new(EntitlementKeys.ReportsStatutory, EntitlementKind.Feature, "Statutory reports",
            "SSS, PhilHealth, Pag-IBIG and BIR remittances, BIR forms and government upload files."),
        new(EntitlementKeys.ApprovalsMultilevel, EntitlementKind.Feature, "Multi-level approvals",
            "Approval workflows with more than one step."),
        new(EntitlementKeys.Analytics, EntitlementKind.Feature, "Analytics", "Analytics dashboards."),
        new(EntitlementKeys.Announcements, EntitlementKind.Feature, "Announcements", "Company announcements."),
        new(EntitlementKeys.AuditTrail, EntitlementKind.Feature, "Audit trail", "The company audit trail."),
        new(EntitlementKeys.ClientsAgency, EntitlementKind.Feature, "Agency clients",
            "Client setup, client billing and the Cost Summary report (agency use)."),
        new(EntitlementKeys.ProcurementCore, EntitlementKind.Feature, "Procurement system",
            "The Procurement system: purchasing, with its own roles and permissions.", PermissionProducts.Procurement),
        new(EntitlementKeys.AccountingCore, EntitlementKind.Feature, "Accounting system",
            "The Accounting system, with its own roles and permissions.", PermissionProducts.Accounting),
        new(EntitlementKeys.BillingCore, EntitlementKind.Feature, "Billing system",
            "The Billing (invoicing) system, with its own roles and permissions.", PermissionProducts.Billing),
        new(EntitlementKeys.ClientPortalCore, EntitlementKind.Feature, "Client Portal",
            "The Client Portal system, with its own roles and permissions.", PermissionProducts.ClientPortal),

        new(EntitlementKeys.CompaniesMax, EntitlementKind.Limit, "Companies",
            "Companies the account may own. Account-wide; admins and biometric devices are per company.", AccountWide),
        new(EntitlementKeys.EmployeesMax, EntitlementKind.Limit, "Employees",
            "Active employees for the whole account, shared by all its companies.", AccountWide),
        new(EntitlementKeys.AdminsMax, EntitlementKind.Limit, "Admins",
            "Members holding any role beyond the portal-only ones (Employee, Client), excluding the owner, per company.", AccountWide),
        new(EntitlementKeys.BiometricDevicesMax, EntitlementKind.Limit, "Biometric devices", "Biometric devices per company.", AccountWide),
        new(EntitlementKeys.StorageMaxMb, EntitlementKind.Limit, "Storage (MB)",
            "File storage in MB for the whole account, shared by all its companies: the plan's base capacity. Purchased storage blocks are added on top.", AccountWide),

        new(EntitlementKeys.SupportTier, EntitlementKind.Display, "Support tier", "Shown on the plan; not enforced.", AccountWide),
    ];

    public static EntitlementInfo? Find(string key) => All.FirstOrDefault(e => e.Key == key);
}
