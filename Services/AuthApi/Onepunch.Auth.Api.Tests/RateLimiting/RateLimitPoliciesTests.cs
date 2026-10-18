using System.Net;
using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Portal;
using OnePunch.Auth.Api.Controllers;
using OnePunch.Auth.Api.RateLimiting;
using Xunit;

namespace Onepunch.Auth.Api.Tests.RateLimiting;

/// <summary>
/// AuthApi's per-endpoint rate limits: who shares an allowance, that it runs out at the limit,
/// what a rejected caller gets back, and which endpoints carry which limit.
/// </summary>
public class RateLimitPoliciesTests
{
    private static HttpContext Request(string ip, string? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (userId != null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)], "test"));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static RateLimitRule Rule(string name) => RateLimitPolicies.Defaults.Single(r => r.Name == name);

    private static System.Threading.RateLimiting.RateLimiter LimiterFor(HttpContext context, RateLimitRule rule)
    {
        var partition = RateLimitPolicies.PartitionFor(context, rule);
        return partition.Factory(partition.PartitionKey);
    }

    [Fact]
    public void UserPolicies_ShareAnAllowancePerUser_AcrossIps() =>
        RateLimitPolicies.PartitionKey(Request("10.0.0.1", "u-1"), RateLimitKey.User)
            .Should().Be(RateLimitPolicies.PartitionKey(Request("10.0.0.2", "u-1"), RateLimitKey.User));

    [Fact]
    public void UserPolicies_WithoutASignedInUser_FallBackToTheIp() =>
        RateLimitPolicies.PartitionKey(Request("10.0.0.1"), RateLimitKey.User).Should().Be("ip:10.0.0.1");

    [Fact]
    public void IpPolicies_IgnoreTheUser() =>
        RateLimitPolicies.PartitionKey(Request("10.0.0.1", "u-1"), RateLimitKey.Ip).Should().Be("ip:10.0.0.1");

    [Fact]
    public void DifferentIps_GetSeparateAllowances() =>
        RateLimitPolicies.PartitionKey(Request("10.0.0.1"), RateLimitKey.Ip)
            .Should().NotBe(RateLimitPolicies.PartitionKey(Request("10.0.0.2"), RateLimitKey.Ip));

    [Theory]
    [InlineData(RateLimitPolicies.Login)]
    [InlineData(RateLimitPolicies.EmailSending)]
    [InlineData(RateLimitPolicies.TokenLookup)]
    [InlineData(RateLimitPolicies.PasswordChange)]
    [InlineData(RateLimitPolicies.InviteSend)]
    [InlineData(RateLimitPolicies.WorkspaceCreate)]
    [InlineData(RateLimitPolicies.PlatformMfa)]
    [InlineData(RateLimitPolicies.PlatformSession)]
    [InlineData(RateLimitPolicies.PlatformAdmin)]
    [InlineData(RateLimitPolicies.PortalLogin)]
    [InlineData(RateLimitPolicies.PortalRefresh)]
    [InlineData(RateLimitPolicies.PortalEmail)]
    [InlineData(RateLimitPolicies.PortalPassword)]
    public void EachPolicy_AllowsItsLimit_ThenRefuses(string name)
    {
        var rule = Rule(name);
        using var limiter = LimiterFor(Request("10.0.0.1", "u-1"), rule);

        for (var i = 0; i < rule.PermitLimit; i++)
            limiter.AttemptAcquire().IsAcquired.Should().BeTrue($"request {i + 1} of {rule.PermitLimit} is within the limit");
        limiter.AttemptAcquire().IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void Limits_CanBeTunedPerEnvironment()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:auth-email:PermitLimit"] = "2",
            ["RateLimiting:auth-email:WindowSeconds"] = "30",
        }).Build();

        var rule = RateLimitPolicies.Configured(Rule(RateLimitPolicies.EmailSending), configuration);

        rule.PermitLimit.Should().Be(2);
        rule.Window.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ARefusedRequest_Gets429_WithRetryAfter_AndAReadableMessage()
    {
        var context = Request("10.0.0.1");
        using var limiter = LimiterFor(context, Rule(RateLimitPolicies.EmailSending) with { PermitLimit = 1 });
        limiter.AttemptAcquire();
        using var refused = limiter.AttemptAcquire();

        await RateLimitPolicies.WriteRejectionAsync(context, refused, CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        int.Parse(context.Response.Headers.RetryAfter!).Should().BeGreaterThan(0);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("Too many requests. Please wait");
    }

    public static TheoryData<Type, string, string> Endpoints => new()
    {
        { typeof(UsersController), nameof(UsersController.Login), RateLimitPolicies.Login },
        { typeof(UsersController), nameof(UsersController.CreateAccount), RateLimitPolicies.EmailSending },
        { typeof(UsersController), nameof(UsersController.ChangePassword), RateLimitPolicies.PasswordChange },
        { typeof(UsersController), nameof(UsersController.SetPassword), RateLimitPolicies.PasswordChange },
        { typeof(InvitationController), nameof(InvitationController.InviteUser), RateLimitPolicies.InviteSend },
        { typeof(InvitationController), nameof(InvitationController.SendBatch), RateLimitPolicies.InviteSend },
        { typeof(InvitationController), nameof(InvitationController.CheckBatch), RateLimitPolicies.InviteCheck },
        { typeof(InvitationController), nameof(InvitationController.AcceptByToken), RateLimitPolicies.TokenLookup },
        { typeof(WorkSpaceController), nameof(WorkSpaceController.Create), RateLimitPolicies.WorkspaceCreate },
        { typeof(PlatformAuthController), nameof(PlatformAuthController.Login), RateLimitPolicies.Login },
        { typeof(PlatformAuthController), nameof(PlatformAuthController.MfaSetup), RateLimitPolicies.PlatformMfa },
        { typeof(PlatformAuthController), nameof(PlatformAuthController.MfaVerify), RateLimitPolicies.PlatformMfa },
        { typeof(PlatformAuthController), nameof(PlatformAuthController.Me), RateLimitPolicies.PlatformSession },
        { typeof(PortalAuthController), nameof(PortalAuthController.Login), RateLimitPolicies.PortalLogin },
        { typeof(PortalAuthController), nameof(PortalAuthController.Refresh), RateLimitPolicies.PortalRefresh },
        { typeof(PortalAuthController), nameof(PortalAuthController.Logout), RateLimitPolicies.PortalRefresh },
        { typeof(PortalAuthController), nameof(PortalAuthController.ForgotPassword), RateLimitPolicies.PortalEmail },
        { typeof(PortalAuthController), nameof(PortalAuthController.ResetPassword), RateLimitPolicies.PortalPassword },
        { typeof(PortalAuthController), nameof(PortalAuthController.EnsureAccount), RateLimitPolicies.PortalPassword },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void Endpoint_CarriesItsLimit(Type controller, string action, string policy) =>
        controller.GetMethod(action)!.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be(policy);

    [Fact]
    public void EveryPolicyAnEndpointNames_IsRegistered()
    {
        var registered = RateLimitPolicies.Defaults.Select(r => r.Name).ToHashSet();
        var used = typeof(UsersController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .OfType<string>();

        used.Should().OnlyContain(p => registered.Contains(p));
    }

    [Fact]
    public void EveryPlatformAction_IsRateLimited_ByTheActionOrItsController()
    {
        var unlimited = new[] { typeof(PlatformAuthController), typeof(PlatformAdminsController) }
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Where(m => m.GetCustomAttribute<EnableRateLimitingAttribute>() == null
                            && c.GetCustomAttribute<EnableRateLimitingAttribute>() == null)
                .Select(m => $"{c.Name}.{m.Name}"));

        unlimited.Should().BeEmpty("a platform endpoint without a limit can be hammered");
    }

    [Fact]
    public void EveryPortalAction_IsRateLimited()
    {
        var unlimited = typeof(PortalAuthController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .Where(m => m.GetCustomAttribute<EnableRateLimitingAttribute>() == null)
            .Select(m => m.Name);

        unlimited.Should().BeEmpty();
    }

    private static HttpContext PortalRequest(string connectionIp, string? key, string? endUserIp, string serviceKey = "svc-key")
    {
        var context = Request(connectionIp);
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<PortalAuthSettings>>(Options.Create(new PortalAuthSettings { ServiceKey = serviceKey }));
        context.RequestServices = services.BuildServiceProvider();
        if (key != null) context.Request.Headers[PortalAuthSettings.KeyHeader] = key;
        if (endUserIp != null) context.Request.Headers[PortalAuthSettings.ClientIpHeader] = endUserIp;
        return context;
    }

    [Fact]
    public void PortalUsers_AreCountedByTheirOwnAddress_NotByThePortalServer()
    {
        var first = RateLimitPolicies.PartitionKey(PortalRequest("10.0.0.9", "svc-key", "203.0.113.1"), RateLimitKey.PortalClient);
        var second = RateLimitPolicies.PartitionKey(PortalRequest("10.0.0.9", "svc-key", "203.0.113.2"), RateLimitKey.PortalClient);

        first.Should().Be("portal:203.0.113.1");
        second.Should().NotBe(first);
    }

    [Theory]
    [InlineData("wrong-key", "203.0.113.1")]
    [InlineData(null, "203.0.113.1")]
    [InlineData("svc-key", "not-an-ip")]
    [InlineData("svc-key", null)]
    public void WithoutTheKeyOrAUsableAddress_TheConnectionsOwnAllowanceApplies(string? key, string? endUserIp) =>
        RateLimitPolicies.PartitionKey(PortalRequest("10.0.0.9", key, endUserIp), RateLimitKey.PortalClient).Should().Be("ip:10.0.0.9");

    [Fact]
    public void WithNoServiceKeyConfigured_TheHeaderIsNeverTrusted() =>
        RateLimitPolicies.PartitionKey(PortalRequest("10.0.0.9", "", "203.0.113.1", serviceKey: ""), RateLimitKey.PortalClient).Should().Be("ip:10.0.0.9");

    [Fact]
    public void PlatformAdminsController_CarriesTheAdminLimit_OnTheController() =>
        typeof(PlatformAdminsController).GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName
            .Should().Be(RateLimitPolicies.PlatformAdmin);
}
