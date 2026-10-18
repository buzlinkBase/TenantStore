using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Onepunch.Common.Lib.Security;
using Onepunch.Common.Web.Authorization;
using Xunit;

namespace TenantStoreApi.Tests.Security;

public class PermissionClaimsTests
{
    private static ClaimsPrincipal User(string[] roles, params string[] permissions)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r))
            .Concat(permissions.Select(p => new Claim("permission", p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Theory]
    [InlineData("Payroll Run:Approve", "hrms")]
    [InlineData("Loan/Deduction:View", "hrms")]
    [InlineData("procurement:Purchase Order:Approve", "procurement")]
    [InlineData("accounting:Journal:Post", "accounting")]
    [InlineData("NoColon", "hrms")]
    public void ProductOf_ReadsTheCodePrefix_AndTreatsUnprefixedCodesAsHrms(string code, string expected)
    {
        PermissionProducts.ProductOf(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("hrms", "Payroll Run", "Approve")]
    [InlineData("procurement", "Purchase Order", "Approve")]
    public void ProductOf_IsTheInverseOfBuildCode(string product, string feature, string action)
    {
        PermissionProducts.ProductOf(PermissionProducts.BuildCode(product, feature, action)).Should().Be(product);
    }

    [Fact]
    public void Owner_HasEveryPermission_OfEveryProduct()
    {
        var owner = User(["Owner"]);

        owner.HasPermission("Payroll Run:Approve").Should().BeTrue();
        owner.HasPermission("procurement:Purchase Order:Approve").Should().BeTrue();
    }

    [Fact]
    public void Admin_HasEveryHrmsPermission_ButNotAnotherProductsUnlessGranted()
    {
        var admin = User(["Admin"]);

        admin.HasPermission("Payroll Run:Approve").Should().BeTrue("HRMS Admin keeps its blanket access");
        admin.HasPermission("procurement:Purchase Order:Approve").Should().BeFalse("an HRMS admin must not open another system by default");
    }

    [Fact]
    public void Admin_HasAnotherProductsPermission_WhenTheClaimIsGranted()
    {
        var admin = User(["Admin"], "procurement:Purchase Order:Approve");

        admin.HasPermission("procurement:Purchase Order:Approve").Should().BeTrue();
        admin.HasPermission("procurement:Purchase Order:Create").Should().BeFalse();
    }

    [Fact]
    public void OtherRoles_NeedTheClaim()
    {
        var member = User(["None"], "Payroll Run:View");

        member.HasPermission("Payroll Run:View").Should().BeTrue();
        member.HasPermission("Payroll Run:Approve").Should().BeFalse();
    }

    [Fact]
    public void HasAnyPermission_IsAnyOf()
    {
        var member = User(["None"], "procurement:Purchase Order:View");

        member.HasAnyPermission("procurement:Purchase Order:Approve", "procurement:Purchase Order:View").Should().BeTrue();
        member.HasAnyPermission("procurement:Purchase Order:Approve").Should().BeFalse();
    }

    private static ActionExecutingContext Context(ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { User = user };
        return new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            [],
            new Dictionary<string, object?>(),
            controller: new object());
    }

    [Fact]
    public async Task RequirePermission_LetsACallerWithTheCodeThrough()
    {
        var context = Context(User(["None"], "procurement:Purchase Order:View"));
        var nextCalled = false;

        await new RequirePermissionAttribute("procurement:Purchase Order:View").OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task RequirePermission_Answers403_NamingTheCodes_WhenTheCallerLacksThem()
    {
        var context = Context(User(["Admin"]));
        var nextCalled = false;

        await new RequirePermissionAttribute("procurement:Purchase Order:View", "procurement:Purchase Order:Create")
            .OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult<ActionExecutedContext>(null!);
            });

        nextCalled.Should().BeFalse();
        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Detail.Should().Contain("procurement:Purchase Order:View");
    }
}
