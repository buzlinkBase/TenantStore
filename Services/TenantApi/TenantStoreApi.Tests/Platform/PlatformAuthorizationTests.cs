using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.Common.Lib.Platform;
using System.Reflection;
using System.Security.Claims;
using TenantStoreApi.Controllers;
using TenantStoreApi.Security;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>
/// The platform (operator) API must only ever be reachable by an operator holding the right
/// permission. These tests fail the build if a platform action is left open, and pin which role can
/// do what.
/// </summary>
public class PlatformAuthorizationTests
{
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformAuth("http://auth-api:8080/");
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Operator(string role, bool mustChangePassword = false, bool authenticatorStepDone = true)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("email", "ops@example.com"),
            new(PlatformAuthDefaults.RoleClaim, role),
            new(PlatformAuthDefaults.MethodClaim, "pwd"),
        };
        if (authenticatorStepDone) claims.Add(new Claim(PlatformAuthDefaults.MethodClaim, PlatformAuthDefaults.OtpMethod));
        if (!mustChangePassword)
            claims.AddRange(PlatformRolePermissions.For(role).Select(p => new Claim(PlatformAuthDefaults.PermissionClaim, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, PlatformAuthDefaults.Scheme));
    }

    private static async Task<bool> CanAsync(ClaimsPrincipal user, string permission)
    {
        using var provider = Services();
        var auth = provider.GetRequiredService<IAuthorizationService>();
        return (await auth.AuthorizeAsync(user, null, permission)).Succeeded;
    }

    // ── Every platform action is locked ──────────────────────────────────────

    private static IEnumerable<(Type Controller, MethodInfo Action)> PlatformActions() =>
        typeof(PlatformBillingController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttributes<RouteAttribute>().Any(r => r.Template.Contains("/platform/", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => (t, m)));

    [Fact]
    public void TheConventionTest_SeesThePlatformBillingActions()
    {
        PlatformActions().Select(a => a.Controller).Should().Contain(typeof(PlatformBillingController));
        PlatformActions().Count().Should().BeGreaterThan(10);
    }

    [Fact]
    public void EveryPlatformAction_RequiresAPlatformPermission_AndIsNeverAnonymous()
    {
        foreach (var (controller, action) in PlatformActions())
        {
            var name = $"{controller.Name}.{action.Name}";
            var attributes = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Concat(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)).ToList();

            action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty($"{name} must not be anonymous");
            controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty($"{controller.Name} must not be anonymous");
            attributes.Should().Contain(a => a is PlatformAuthorizeAttribute, $"{name} needs [PlatformAuthorize(permission)]");

            var policy = attributes.OfType<PlatformAuthorizeAttribute>().First().Policy;
            PlatformPermissions.All.Should().Contain(policy, $"{name} must name a real platform permission");
            attributes.OfType<PlatformAuthorizeAttribute>().First().AuthenticationSchemes.Should().Be(PlatformAuthDefaults.Scheme);
        }
    }

    // ── What each role may do ────────────────────────────────────────────────

    [Theory]
    [InlineData(PlatformRoles.SuperAdmin, PlatformPermissions.AccountsRead, true)]
    [InlineData(PlatformRoles.SuperAdmin, PlatformPermissions.SettingsManage, true)]
    [InlineData(PlatformRoles.SuperAdmin, PlatformPermissions.AdminsManage, true)]
    [InlineData(PlatformRoles.Billing, PlatformPermissions.PaymentsRecord, true)]
    [InlineData(PlatformRoles.Billing, PlatformPermissions.PlansManage, true)]
    [InlineData(PlatformRoles.Billing, PlatformPermissions.SettingsManage, false)]
    [InlineData(PlatformRoles.Billing, PlatformPermissions.AdminsManage, false)]
    [InlineData(PlatformRoles.Support, PlatformPermissions.AccountsRead, true)]
    [InlineData(PlatformRoles.Support, PlatformPermissions.AuditRead, true)]
    [InlineData(PlatformRoles.Support, PlatformPermissions.PaymentsRecord, false)]
    [InlineData(PlatformRoles.Support, PlatformPermissions.SubscriptionsManage, false)]
    [InlineData(PlatformRoles.Support, PlatformPermissions.PlansManage, false)]
    public async Task ARole_HoldsExactlyItsPermissions(string role, string permission, bool expected)
    {
        (await CanAsync(Operator(role), permission)).Should().Be(expected);
    }

    [Fact]
    public async Task ATokenThatNeverPassedTheAuthenticatorStep_CanDoNothing()
    {
        var passwordOnly = Operator(PlatformRoles.SuperAdmin, authenticatorStepDone: false);

        foreach (var permission in PlatformPermissions.All)
            (await CanAsync(passwordOnly, permission)).Should().BeFalse($"{permission} needs the authenticator step");
    }

    [Fact]
    public async Task AnOperatorWhoMustStillChangeTheirPassword_CanDoNothing()
    {
        var user = Operator(PlatformRoles.SuperAdmin, mustChangePassword: true);

        foreach (var permission in PlatformPermissions.All)
            (await CanAsync(user, permission)).Should().BeFalse($"{permission} must fail closed until the password is changed");
    }

    [Fact]
    public async Task ACompanyUsersToken_IsNeverEnough_EvenWithLookAlikeClaims()
    {
        // Company users carry permissions such as "Billing:View" and role "Owner"; none of that is a platform permission.
        var companyUser = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Owner"),
            new Claim("permission", "Billing:View"),
            new Claim("permission", "Billing:Manage"),
            new Claim("tenantId", Guid.NewGuid().ToString()),
        ], "Bearer"));

        foreach (var permission in PlatformPermissions.All)
            (await CanAsync(companyUser, permission)).Should().BeFalse();
    }

    [Fact]
    public async Task AnUnauthenticatedCaller_IsDenied()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        foreach (var permission in PlatformPermissions.All)
            (await CanAsync(anonymous, permission)).Should().BeFalse();
    }

    [Fact]
    public void RegisteringThePlatformScheme_LeavesTheCompanyUsersSchemeAsTheDefault()
    {
        // Mirrors RegisterSelfServices: Bearer is the explicit default, then the platform scheme is added.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(options => options.DefaultAuthenticateScheme = "Bearer").AddJwtBearer();
        services.AddPlatformAuth("http://auth-api:8080/");
        using var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();

        schemes.GetSchemeAsync(PlatformAuthDefaults.Scheme).GetAwaiter().GetResult().Should().NotBeNull();
        schemes.GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult()!.Name.Should().Be("Bearer");
    }
}
