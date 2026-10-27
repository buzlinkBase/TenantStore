using System.Security.Claims;
using FluentAssertions;
using OnePunch.Auth.Api.Controllers;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Controllers;

public class WorkSpaceControllerTests
{
    private static ClaimsPrincipal User(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));

    [Fact]
    public void CanCreateCompany_AllowsTheOwner()
    {
        WorkSpaceController.CanCreateCompany(User("Owner")).Should().BeTrue();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Employee")]
    [InlineData("Member")]
    [InlineData("Client")]
    public void CanCreateCompany_RefusesAnyoneInsideACompanyWhoIsNotItsOwner(string role)
    {
        WorkSpaceController.CanCreateCompany(User(role)).Should().BeFalse();
    }

    [Fact]
    public void CanCreateCompany_AllowsSomeoneWithNoCompanyYetToCreateTheirFirst()
    {
        WorkSpaceController.CanCreateCompany(User()).Should().BeTrue();
    }

    [Fact]
    public void CanCreateCompany_AllowsAnOwnerWhoAlsoHoldsAnotherRole()
    {
        WorkSpaceController.CanCreateCompany(User("Admin", "Owner")).Should().BeTrue();
    }
}
