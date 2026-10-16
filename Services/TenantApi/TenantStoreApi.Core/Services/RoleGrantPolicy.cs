namespace TenantStoreApi.Core.Services;

/// <summary>
/// No privilege escalation: a member may only grant (by invitation or by editing someone's roles)
/// a role whose permissions they already hold themselves. The Owner may grant anything. The
/// baseline Employee and Client system roles -- what every employee / client invitation gets by
/// default -- are always grantable by whoever may invite or edit roles at all.
/// </summary>
public static class RoleGrantPolicy
{
    public static readonly IReadOnlySet<string> BaselineRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Employee", "Client" };

    public static bool CanGrant(
        bool callerIsOwner,
        IReadOnlySet<string> callerCodes,
        string roleName,
        bool isSystemRole,
        IEnumerable<string> roleCodes) =>
        callerIsOwner
        || (isSystemRole && BaselineRoles.Contains(roleName))
        || roleCodes.All(callerCodes.Contains);

    public static string DeniedMessage(string roleName) =>
        $"You can't grant the '{roleName}' role: it includes permissions you don't have.";

    public static string DeniedManageMessage(string roleName) =>
        $"You can't change this person: their '{roleName}' role includes permissions you don't have.";
}
