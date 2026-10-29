namespace Onepunch.Common.Lib.Platform;

/// <summary>
/// Which company users a platform operator may sign in as. The ceiling is by operator role and is
/// data, like <see cref="PlatformRolePermissions"/>: a SuperAdmin may impersonate anyone, including a
/// company Owner; Support may not impersonate an Owner or Admin. A role with no entry (Billing, an
/// unknown role) can impersonate no one -- the check fails closed.
/// </summary>
public static class ImpersonationPolicy
{
    private const string Owner = "Owner";
    private const string Admin = "Admin";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> BlockedTargetRoles =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [PlatformRoles.SuperAdmin] = [],
            [PlatformRoles.Support] = [Owner, Admin],
        };

    /// <summary><paramref name="targetRoles"/> are the target's roles in the company being entered.</summary>
    public static bool CanImpersonate(string? operatorRole, IEnumerable<string> targetRoles)
    {
        if (operatorRole == null || !BlockedTargetRoles.TryGetValue(operatorRole, out var blocked)) return false;
        return !targetRoles.Any(role => blocked.Contains(role, StringComparer.OrdinalIgnoreCase));
    }
}
