using FluentAssertions;
using TenantStoreApi.Domain.Entities;
using Xunit;

namespace TenantStoreApi.Tests.Entities;

/// <summary>
/// UserMembershipExtensions.HasPermission/HasAnyPermission -- the real, permission-based gate
/// used by UserMembershipService (AddOrUpdateRoleAsync, RemoveRoleAsync, ReplaceRolesAsync,
/// ChangeStatusAsync, RemoveMemberAsync) and RolesController.CallerCanManageRolesAsync. Fails
/// closed when a role's RolePermissions haven't been loaded/backfilled yet -- except Owner/Admin,
/// who keep an explicit HasAnyRole fallback so a not-yet-backfilled membership (e.g. the brief
/// window right after workspace creation) doesn't 403 the very roles those callers' own error
/// messages ("Only Owner or Admin can...") already promise are allowed.
/// </summary>
public class UserMembershipTests
{
    private static UserMembership WithRole(string role, RolePermission[]? rolePermissions = null) => new()
    {
        Roles =
        [
            new MembershipRole
            {
                Role = role,
                RoleRef = rolePermissions == null ? null : new Role { Name = role, RolePermissions = rolePermissions },
            },
        ],
    };

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public void HasPermission_ReturnsTrue_ForOwnerOrAdmin_WhenRoleRefIsNotLoadedAtAll(string role)
    {
        // RoleRef == null is exactly what a not-yet-backfilled/not-yet-loaded membership looks
        // like -- the scenario the permission-based lookup alone fails closed on.
        WithRole(role).HasPermission("Tenant Members:Manage").Should().BeTrue();
    }

    [Fact]
    public void HasPermission_ReturnsFalse_ForOtherRoles_WhenRoleRefIsNotLoaded()
    {
        WithRole("Member").HasPermission("Tenant Members:Manage").Should().BeFalse();
    }

    [Fact]
    public void HasPermission_ReturnsTrue_WhenRolePermissionsActuallyGrantTheCode()
    {
        var permission = new Permission { Code = "Tenant Members:Manage" };
        var membership = WithRole("Member", [new RolePermission { Permission = permission }]);

        membership.HasPermission("Tenant Members:Manage").Should().BeTrue();
    }

    [Fact]
    public void HasAnyPermission_ReturnsTrue_ForOwner_RegardlessOfRequestedCodes()
    {
        WithRole("Owner").HasAnyPermission("Tenant Members:Manage", "Users:Edit").Should().BeTrue();
    }
}
