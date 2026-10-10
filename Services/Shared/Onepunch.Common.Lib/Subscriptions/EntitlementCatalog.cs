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

/// <summary>One entitlement as the platform console shows it: what it is called and what it controls.</summary>
public sealed record EntitlementInfo(string Key, EntitlementKind Kind, string Label, string Description);

/// <summary>
/// The human-readable side of <see cref="EntitlementKeys"/>, so the console can render a plan's
/// entitlements without hardcoding their names. A test keeps the two in step: every key has an entry.
/// </summary>
public static class EntitlementCatalog
{
    public static readonly IReadOnlyList<EntitlementInfo> All =
    [
        new(EntitlementKeys.TimekeepingCore, EntitlementKind.Feature, "Timekeeping",
            "Attendance upload and manual entry, logs, shifts, rotation plans, schedule changes, DTR generation."),
        new(EntitlementKeys.TimekeepingBiometric, EntitlementKind.Feature, "Biometric devices",
            "Biometric (ADMS) devices and enrollment."),
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

        new(EntitlementKeys.CompaniesMax, EntitlementKind.Limit, "Companies",
            "Companies the account may own. Account-wide; admins and biometric devices are per company."),
        new(EntitlementKeys.EmployeesMax, EntitlementKind.Limit, "Employees",
            "Active employees for the whole account, shared by all its companies."),
        new(EntitlementKeys.AdminsMax, EntitlementKind.Limit, "Admins",
            "Members holding any role beyond the portal-only ones (Employee, Client), excluding the owner, per company."),
        new(EntitlementKeys.BiometricDevicesMax, EntitlementKind.Limit, "Biometric devices", "Biometric devices per company."),
        new(EntitlementKeys.StorageMaxMb, EntitlementKind.Limit, "Storage (MB)",
            "File storage in MB for the whole account, shared by all its companies: the plan's base capacity. Purchased storage blocks are added on top."),

        new(EntitlementKeys.SupportTier, EntitlementKind.Display, "Support tier", "Shown on the plan; not enforced."),
    ];

    public static EntitlementInfo? Find(string key) => All.FirstOrDefault(e => e.Key == key);
}
