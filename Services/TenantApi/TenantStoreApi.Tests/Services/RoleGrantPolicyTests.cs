using FluentAssertions;
using TenantStoreApi.Core.Services;
using Xunit;

namespace TenantStoreApi.Tests.Services;

/// <summary>No privilege escalation: only roles whose permissions the granter holds, bar the Owner.</summary>
public class RoleGrantPolicyTests
{
    private static readonly IReadOnlySet<string> Caller = new HashSet<string> { "Users:View", "Users:Invite", "Leave:View" };

    [Fact]
    public void RoleWithinTheCallersPermissions_IsGrantable() =>
        RoleGrantPolicy.CanGrant(false, Caller, "Leave Clerk", false, ["Leave:View"]).Should().BeTrue();

    [Fact]
    public void RoleWithAnyPermissionTheCallerLacks_IsNot() =>
        RoleGrantPolicy.CanGrant(false, Caller, "HR", false, ["Leave:View", "Payroll Run:Approve"]).Should().BeFalse();

    [Fact]
    public void RoleWithNoPermissions_IsGrantable() =>
        RoleGrantPolicy.CanGrant(false, Caller, "Member", true, []).Should().BeTrue();

    [Fact]
    public void Owner_MayGrantAnything() =>
        RoleGrantPolicy.CanGrant(true, new HashSet<string>(), "Admin", true, ["Tenant Members:Manage"]).Should().BeTrue();

    [Theory]
    [InlineData("Employee")]
    [InlineData("client")]
    public void BaselineSystemRoles_AreAlwaysGrantable(string role) =>
        RoleGrantPolicy.CanGrant(false, new HashSet<string>(), role, true, ["Employee Self-Service Portal:View"]).Should().BeTrue();

    [Fact]
    public void ACustomRoleNamedLikeABaselineRole_GetsNoPass() =>
        RoleGrantPolicy.CanGrant(false, new HashSet<string>(), "Employee", false, ["Payroll Run:Approve"]).Should().BeFalse();
}
