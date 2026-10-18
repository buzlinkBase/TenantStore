using Onepunch.Common.Lib.Security;

namespace TenantStoreApi.Core.Services;

/// <summary>Names of the roles client-portal users hold (RoleAudience.Client system roles).</summary>
public static class ClientRoles
{
    public const string Admin = "Client Admin";
    public const string Executive = "Client Executive";
    public const string FinanceManager = "Finance Manager";
    public const string SiteSupervisor = "Site Supervisor";
}

// The Client Portal product's slice of the permission catalog. Codes are product-prefixed
// ("clientportal:Client:Manage"). It holds two separate sets that share the prefix:
//
//  * Agency side -- Client / Billing / Operations. The portal API checks these strings on the HRIS
//    token an agency user brings, and the HRIS Billing Manager screens show their menu items on it.
//    Nothing is granted to Admin: a separate system is not opened by being an HRMS admin. The Owner
//    gets every permission, and grants these to other roles from the Roles screen.
//
//  * Client side -- what a client's own users can do inside the portal (Users, Roles, Time Logs,
//    Invoices ...). They are held through Client_Membership_Roles by Category=ClientUser members and
//    carried by the RoleAudience.Client roles below; the agency's role editor never offers them to
//    staff. The portal's module switches still decide which of them are in effect for a client.
public class ClientPortalPermissionCatalog : IPermissionCatalogProvider
{
    private const string ClientModule = "Client Users";

    public string Product => PermissionProducts.ClientPortal;

    private static readonly (string Module, string Feature, string[] Actions)[] FeatureRows =
    [
        // Create portal clients and sections, manage client users, API keys, and verify DTR periods.
        ("Client Portal", "Client", ["Manage"]),
        // Billing is split so the person who prepares an invoice is not automatically the one who approves or sends it.
        ("Client Portal", "Billing", ["View", "Manage", "Approve", "Issue"]),
        // Guard documents, notices, ratings scorecards and manpower requests.
        ("Client Portal", "Operations", ["View", "Manage"]),

        (ClientModule, "Users", ["View", "Invite", "AssignRoles", "Manage"]),
        (ClientModule, "Roles", ["Manage"]),
        (ClientModule, "Time Logs", ["View", "Review", "SignOff", "Export"]),
        (ClientModule, "Invoices", ["View", "Download"]),
        (ClientModule, "Payments", ["View", "SubmitProof"]),
        (ClientModule, "Disputes", ["View", "Create"]),
        (ClientModule, "Reports", ["View"]),
        (ClientModule, "Manpower", ["Request"]),
        (ClientModule, "Document Vault", ["View"]),
        (ClientModule, "Ratings", ["Submit"]),
        (ClientModule, "Notices", ["View"]),
    ];

    public IReadOnlyList<(string Module, string Feature, string[] Actions)> Features => FeatureRows;

    /// <summary>The client-side codes, i.e. everything under the Client Users module.</summary>
    public static IReadOnlyList<string> ClientCodes { get; } = BuildClientCodes();

    public IReadOnlyList<SystemRoleDefinition> SystemRoles { get; } =
    [
        Role(ClientRoles.Admin, "Full access to everything the client's enabled modules offer."),
        Role(ClientRoles.Executive, "Oversight of time logs, billing and reports."),
        Role(ClientRoles.FinanceManager, "Invoices, payments and disputes."),
        Role(ClientRoles.SiteSupervisor, "Reviews time logs of assigned sites."),
    ];

    public IReadOnlyDictionary<string, string[]> RoleGrants { get; } = new Dictionary<string, string[]>
    {
        [ClientRoles.Admin] = [.. ClientCodes],
        [ClientRoles.Executive] =
        [
            Code("Users", "View"), Code("Time Logs", "View"), Code("Time Logs", "Review"), Code("Time Logs", "SignOff"),
            Code("Time Logs", "Export"), Code("Invoices", "View"), Code("Invoices", "Download"), Code("Payments", "View"),
            Code("Disputes", "View"), Code("Reports", "View"), Code("Notices", "View"), Code("Ratings", "Submit"),
        ],
        [ClientRoles.FinanceManager] =
        [
            Code("Invoices", "View"), Code("Invoices", "Download"), Code("Payments", "View"), Code("Payments", "SubmitProof"),
            Code("Disputes", "View"), Code("Disputes", "Create"), Code("Reports", "View"), Code("Time Logs", "View"),
        ],
        [ClientRoles.SiteSupervisor] =
        [
            Code("Time Logs", "View"), Code("Time Logs", "Review"), Code("Time Logs", "SignOff"), Code("Time Logs", "Export"),
            Code("Manpower", "Request"), Code("Document Vault", "View"), Code("Notices", "View"), Code("Ratings", "Submit"),
        ],
    };

    private static SystemRoleDefinition Role(string name, string description) =>
        new(name, description, PermissionProducts.ClientPortal, RoleAudience.Client);

    private static string Code(string feature, string action) =>
        PermissionProducts.BuildCode(PermissionProducts.ClientPortal, feature, action);

    private static List<string> BuildClientCodes() =>
        FeatureRows
            .Where(f => f.Module == ClientModule)
            .SelectMany(f => f.Actions.Select(a => Code(f.Feature, a)))
            .ToList();
}
