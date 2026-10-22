using Onepunch.Common.Lib.Security;

namespace TenantStoreApi.Core.Services;

// The HRMS product's slice of the permission catalog. Codes stay unprefixed ("Payroll Run:Approve")
// so every existing role grant, JWT claim and RequirePermission string keeps working.
public class HrmsPermissionCatalog : IPermissionCatalogProvider
{
    public string Product => PermissionProducts.Hrms;

    public IReadOnlyList<(string Module, string Feature, string[] Actions)> Features => Catalog;

    // Owner/Admin/Member/Employee/Client are cross-product and seeded by PermissionCatalogSeederService.
    public IReadOnlyList<SystemRoleDefinition> SystemRoles => [];

    public IReadOnlyDictionary<string, string[]> RoleGrants { get; } = new Dictionary<string, string[]>
    {
        [TenantRoles.Admin] = AdminGrantedCodes,
        [TenantRoles.Employee] = EmployeeGrantedCodes,
    };
    private static readonly (string Module, string Feature, string[] Actions)[] Catalog =
    [
        // Gates the frontend's Dashboard menu/page like every other screen -- a role with no
        // permissions assigned (e.g. plain Member) shouldn't see it either.
        ("Dashboard", "Dashboard", ["View"]),

        ("Setup", "Organization Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Workforce Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Time Shift Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Deductions & Income Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Leave Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Statutory Tables", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Biometric Setup", ["View", "Create", "Edit", "Delete"]),
        // New rows -- Holidays and Minimum Wage Rates were the only Setup screens with no
        // catalog entry, which left their menu items visible to every member (even one with no
        // permissions at all) because there was nothing to gate them on.
        ("Setup", "Holiday Setup", ["View", "Create", "Edit", "Delete"]),
        ("Setup", "Minimum Wage Setup", ["View", "Create", "Edit", "Delete"]),

        // Edit: upload attendance files, and edit (or restore the original of) a file-uploaded
        // log from the DTR Attendance Logs view. Delete: remove a file-uploaded log, or a whole
        // upload (AttendanceController.DeleteUploadBatch).
        ("Timekeeping", "Upload Attendance", ["View", "Edit", "Export", "Delete"]),
        // Delete: AttendanceController.Delete(id)/Delete(batch) remove manually-entered
        // attendance records/batches -- had no catalog action to gate with before this.
        ("Timekeeping", "Attendance Manual Entry", ["View", "Edit", "Export", "Delete"]),
        // Logs synced from biometric devices. Edit/Delete: edit or remove (and restore the
        // original of) a synced log from the DTR Attendance Logs view. View: reserved for a
        // synced-logs screen.
        ("Timekeeping", "Sync Attendance", ["View", "Edit", "Delete"]),
        ("Timekeeping", "Raw Logs", ["View", "Edit", "Export"]),
        ("Timekeeping", "Unregistered Employees", ["View", "Edit", "Export"]),
        ("Timekeeping", "Incomplete Punches", ["View", "Edit", "Export"]),

        // ManageOwnTeam: granted instead of (not alongside) Create to a Supervisor-style
        // Custom Role -- lets them schedule Work Rotation only for their own direct reports
        // (Employee.ManagerId in hrms-api), not the whole company. Create remains "assign for
        // anyone," unscoped, same as before. Approve: WorkSchedulePlansController has no
        // approve endpoint today (only Get/PostBatch/Delete/DeleteBatch are live) -- this stays
        // a dormant/reserved action until that feature actually exists, same as Change
        // Holiday's Approve below.
        ("Change Schedule", "Work Rotation", ["View", "Create", "Approve", "ManageOwnTeam", "Delete"]),
        ("Change Schedule", "Change Rest Day", ["View", "Create", "Approve", "Delete"]),
        // Approve: ChangeHolidaysController has no approve/decline action at all -- dormant,
        // same reasoning as Work Rotation's Approve above.
        ("Change Schedule", "Change Holiday", ["View", "Create", "Approve", "Delete"]),

        // Approve gates ApproveBatch/DeclineBatch on the DTR posting approval flow -- see
        // DailyRecordsController. DTR Summary has no equivalent action (it's a read-only report).
        ("DTR Generation", "DTR Master", ["View", "Manage", "Approve"]),
        ("DTR Generation", "DTR Summary", ["View", "Manage"]),

        ("Payroll Generation", "Payroll Run", ["View", "Create", "Approve", "Export"]),
        ("Payroll Generation", "Payroll Summary", ["View", "Create", "Approve", "Export"]),
        ("Payroll Generation", "13th Month Run", ["View", "Create", "Approve", "Export"]),
        ("Payroll Generation", "Last Pay Run", ["View", "Create", "Approve", "Export"]),
        ("Payroll Generation", "Year-End Adjustment Run", ["View", "Create", "Approve", "Export"]),

        // Create/Edit added: each row's admin controller (e.g. LeaveApplicationsController) has
        // its own admin-side Post/Put actions, entirely separate from employee self-service
        // (which lives on MeController under me/...) -- these had nothing to gate with before.
        // Approve stays dormant for Other Income and Salary Adjustment specifically -- neither
        // entity has an approval workflow in the backend at all.
        ("Applications", "Leave", ["View", "Create", "Edit", "Approve", "Delete"]),
        ("Applications", "Overtime", ["View", "Create", "Edit", "Approve", "Delete"]),
        ("Applications", "Official Business", ["View", "Create", "Edit", "Approve", "Delete"]),
        // New row -- UnderTimeApplicationsController is a full sibling of Overtime (identical
        // shape) that was simply never added to the catalog.
        ("Applications", "Undertime", ["View", "Create", "Edit", "Approve", "Delete"]),
        ("Applications", "Pass Slip", ["View", "Create", "Edit", "Approve", "Delete"]),
        ("Applications", "Loan/Deduction", ["View", "Create", "Edit", "Approve", "Delete"]),
        ("Applications", "Other Income", ["View", "Create", "Edit", "Approve", "Delete"]),
        ("Applications", "Salary Adjustment", ["View", "Create", "Edit", "Approve", "Delete"]),
        // No Create/Edit/Delete: employees submit their own via the Employee Portal
        // (MeController's me/profile-update-requests), HR/admin only reviews and decides.
        ("Applications", "Profile Update", ["View", "Approve"]),
        // Setup screen for the configurable multi-level approval engine (Leave/Overtime/Official
        // Business/Pass Slip/Loan-Deduction route through whatever's configured here; unconfigured
        // types keep today's single-step behavior). View/Edit only -- no Create/Delete/Approve,
        // matching the other Setup-shaped config screens (e.g. Company Policy).
        ("Applications", "Approval Workflows", ["View", "Edit"]),

        ("Reports", "Government Statutory Reports", ["View", "Export"]),
        // Edit: RetirementController.Adjust and UniformAllowanceController.Adjust/Release are
        // real mutations embedded in the Retirement/Uniform Allowance Ledger report screens --
        // had no Create/Edit action anywhere in Reports to gate them with before.
        ("Reports", "Payroll Reports", ["View", "Export", "Edit"]),
        ("Reports", "BIR Reports", ["View", "Export"]),
        // New row -- Tardiness/Rostering (DailyRecordsController.TardinessReport/RosterReport)
        // don't fit any of the 3 rows above by subject matter (not statutory, not payroll-cycle,
        // not BIR); no Export variant exists for either today.
        ("Reports", "Attendance Reports", ["View"]),

        // Analytics module (hrms-api AnalyticsController): trends and breakdowns per area. Pay
        // figures still follow Confidential Records -- these codes grant the screens, not the
        // confidential employees in them.
        ("Analytics", "Workforce Analytics", ["View", "Export"]),
        ("Analytics", "Attendance Analytics", ["View", "Export"]),
        ("Analytics", "Payroll Analytics", ["View", "Export"]),
        ("Analytics", "Request Analytics", ["View", "Export"]),

        // Invite: send company invitations, single or batch (AuthApi InvitationController).
        // AssignRole: pick a role other than the default (Employee / Client) when inviting --
        // still limited to roles whose permissions the inviter holds (RoleGrantPolicy).
        ("Security", "Users", ["View", "Create", "Edit", "Delete", "Manage", "Invite", "AssignRole"]),
        ("Security", "Roles", ["View", "Create", "Edit", "Delete", "Manage"]),
        ("Security", "Permissions", ["View", "Create", "Edit", "Delete", "Manage"]),
        ("Security", "Audit Trail", ["View", "Create", "Edit", "Delete", "Manage"]),
        // Clearance for employees tagged Confidential (hrms-api ConfidentialAccessScope). Access:
        // everything. Operations: their DTR, attendance, schedules and applications. Entries: their
        // cash bond, allowances / other income and loans. Pay, payroll runs, payslips and pay-based
        // reports stay Access-only. Owner always has all three; deliberately NOT in
        // AdminGrantedCodes -- the Owner decides which roles are cleared.
        ("Security", "Confidential Records", ["Access", "Operations", "Entries"]),

        ("Employee Portal", "Employee Self-Service Portal", ["View", "Manage"]),

        // Company announcements to employees (hrms-api AnnouncementsController). Publish covers
        // the whole send lifecycle -- publish/schedule, unpublish/republish, remind, pin,
        // archive/restore; Delete covers the bin (delete/restore-deleted). Receiving and reading
        // announcements needs no permission.
        ("Communication", "Announcements", ["View", "Create", "Edit", "Delete", "Publish"]),

        // The account's subscription (TenantApi SubscriptionsController): View sees the plan,
        // prices, usage and payment history; Manage changes plan, cancels or resumes. Owner has
        // both; deliberately NOT in AdminGrantedCodes -- pricing and billing are for the owner
        // and whoever they make a billing admin through a custom role.
        ("Billing", "Billing", ["View", "Manage"]),

        // Billing > Storage (hrms-api StorageManagerController): View sees what fills the
        // company's file storage by category and year; Manage deletes files no longer needed, so
        // the account uses the storage it has instead of buying more. Unlike Billing it IS in
        // AdminGrantedCodes -- cleaning up files is day-to-day administration, not spending.
        ("Billing", "Storage", ["View", "Manage"]),

        // What UserMembershipService already enforces today via HasAnyRole(Owner, Admin) --
        // folded into the same catalog so it's one permission system, not two. Named "Tenant
        // Members"/"Tenant Roles" (not "Members"/"Roles") to avoid colliding with the Security
        // module's "Users"/"Roles" features above, which are a different concept (the HR
        // product's own admin screens) even though the words overlap.
        ("Tenant Administration", "Tenant Members", ["Manage"]),
        ("Tenant Administration", "Tenant Roles", ["Manage"]),
    ];

    // "Tenant Administration" (member/role management) plus every module below -- Admin has
    // always had de facto full access everywhere (nothing checked permissions before each
    // module's own enforcement pass), so each addition here keeps that true now that something
    // does. Every module groups features by identical action set into one SelectMany -- rows
    // whose action set differs from their siblings (e.g. Attendance Manual Entry's extra
    // Delete, Work Rotation's extra ManageOwnTeam, Payroll Reports' extra Edit) are granted
    // individually instead of forced into a group they don't quite match.
    private static readonly string[] AdminGrantedCodes =
    [
        "Tenant Members:Manage", "Tenant Roles:Manage",
        "Dashboard:View",
        .. new[]
        {
            "Organization Setup", "Workforce Setup", "Time Shift Setup",
            "Deductions & Income Setup", "Leave Setup", "Statutory Tables", "Biometric Setup",
            "Holiday Setup", "Minimum Wage Setup",
        }.SelectMany(feature => new[] { "View", "Create", "Edit", "Delete" }.Select(action => $"{feature}:{action}")),
        // Security module -- its menu items are now permission-gated like every other module,
        // so Admin needs these explicitly to keep the Users/Roles/Permissions/Audit screens.
        .. new[]
        {
            "Users", "Roles", "Permissions", "Audit Trail",
        }.SelectMany(feature => new[] { "View", "Create", "Edit", "Delete", "Manage" }.Select(action => $"{feature}:{action}")),
        "Users:Invite", "Users:AssignRole",
        .. new[]
        {
            "Payroll Run", "Payroll Summary", "13th Month Run", "Last Pay Run", "Year-End Adjustment Run",
        }.SelectMany(feature => new[] { "View", "Create", "Approve", "Export" }.Select(action => $"{feature}:{action}")),
        .. new[]
        {
            "Upload Attendance", "Raw Logs", "Unregistered Employees", "Incomplete Punches",
        }.SelectMany(feature => new[] { "View", "Edit", "Export" }.Select(action => $"{feature}:{action}")),
        "Upload Attendance:Delete",
        .. new[] { "View", "Edit", "Export", "Delete" }.Select(action => $"Attendance Manual Entry:{action}"),
        .. new[] { "View", "Edit", "Delete" }.Select(action => $"Sync Attendance:{action}"),
        .. new[] { "View", "Create", "Approve", "ManageOwnTeam", "Delete" }.Select(action => $"Work Rotation:{action}"),
        .. new[]
        {
            "Change Rest Day", "Change Holiday",
        }.SelectMany(feature => new[] { "View", "Create", "Approve", "Delete" }.Select(action => $"{feature}:{action}")),
        .. new[] { "View", "Manage", "Approve" }.Select(action => $"DTR Master:{action}"),
        .. new[] { "View", "Manage" }.Select(action => $"DTR Summary:{action}"),
        .. new[]
        {
            "Leave", "Overtime", "Official Business", "Undertime", "Pass Slip",
            "Loan/Deduction", "Other Income", "Salary Adjustment",
        }.SelectMany(feature => new[] { "View", "Create", "Edit", "Approve", "Delete" }.Select(action => $"{feature}:{action}")),
        .. new[] { "View", "Approve" }.Select(action => $"Profile Update:{action}"),
        .. new[] { "View", "Edit" }.Select(action => $"Approval Workflows:{action}"),
        .. new[]
        {
            "Government Statutory Reports", "BIR Reports",
        }.SelectMany(feature => new[] { "View", "Export" }.Select(action => $"{feature}:{action}")),
        .. new[] { "View", "Export", "Edit" }.Select(action => $"Payroll Reports:{action}"),
        "Attendance Reports:View",
        .. new[]
        {
            "Workforce Analytics", "Attendance Analytics", "Payroll Analytics", "Request Analytics",
        }.SelectMany(feature => new[] { "View", "Export" }.Select(action => $"{feature}:{action}")),
        .. new[] { "View", "Create", "Edit", "Delete", "Publish" }.Select(action => $"Announcements:{action}"),
        "Storage:View", "Storage:Manage",
    ];
    // The frontend's "My Portal" menu is gated on this permission (see hrms-ui-onepunch's
    // navigation.const.ts) -- Employee is the role whose entire purpose is the self-service
    // portal, so it's granted by default rather than left for a tenant to assign manually.
    private static readonly string[] EmployeeGrantedCodes = ["Employee Self-Service Portal:View"];
}
