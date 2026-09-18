using FluentAssertions;
using OnePunch.Auth.Core.Services;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// UserService.ResolveDefaultRoles -- the rule behind ChangedRoles that keeps
/// User.DefaultTenantRoles (the fallback cache MembershipCacheService.FallbackToDefaultTenantAsync
/// serves when Tenant Service is unreachable) from ever pointing at a role the user no longer
/// holds after an edit to their default tenant's role set.
/// </summary>
public class UserServiceResolveDefaultRolesTests
{
    [Fact]
    public void KeepsCurrentDefaults_WhenStillPresentInTheNewRoleSet()
    {
        var result = UserService.ResolveDefaultRoles(["Admin"], ["Admin", "Member"]);

        result.Should().BeEquivalentTo(["Admin"]);
    }

    [Fact]
    public void FallsBackToFirstNewRole_WhenNoCurrentDefaultSurvived()
    {
        var result = UserService.ResolveDefaultRoles(["Admin"], ["Member", "Employee"]);

        result.Should().BeEquivalentTo(["Member"]);
    }

    [Fact]
    public void KeepsOnlyTheSurvivingSubset_WhenSomeCurrentDefaultsWereRemoved()
    {
        var result = UserService.ResolveDefaultRoles(["Admin", "Member"], ["Member", "Employee"]);

        result.Should().BeEquivalentTo(["Member"]);
    }

    [Fact]
    public void FallsBackToFirstNewRole_WhenThereWereNoCurrentDefaultsAtAll()
    {
        var result = UserService.ResolveDefaultRoles([], ["Member", "Employee"]);

        result.Should().BeEquivalentTo(["Member"]);
    }
}
