using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Reflection;
using TenantStoreApi.Controllers;
using TenantStoreApi.Security;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>The platform API's rate limit: every platform controller carries it, it runs out, and it can be tuned.</summary>
public class PlatformRateLimitingTests
{
    private static HttpContext Request(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public void EveryPlatformController_IsRateLimited()
    {
        var unlimited = typeof(PlatformBillingController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttributes<RouteAttribute>().Any(r => r.Template.Contains("/platform/", StringComparison.OrdinalIgnoreCase)))
            .Where(t => t.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName != PlatformRateLimiting.PolicyName)
            .Select(t => t.Name);

        unlimited.Should().BeEmpty("a platform endpoint without a limit can be hammered");
    }

    [Fact]
    public void ALimiter_AllowsItsLimit_ThenRefuses()
    {
        var partition = PlatformRateLimiting.PartitionFor(Request("10.0.0.1"), 3, TimeSpan.FromMinutes(1));
        using var limiter = partition.Factory(partition.PartitionKey);

        for (var i = 0; i < 3; i++)
            limiter.AttemptAcquire().IsAcquired.Should().BeTrue();
        limiter.AttemptAcquire().IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void DifferentIps_GetSeparateAllowances() =>
        PlatformRateLimiting.PartitionFor(Request("10.0.0.1"), 3, TimeSpan.FromMinutes(1)).PartitionKey
            .Should().NotBe(PlatformRateLimiting.PartitionFor(Request("10.0.0.2"), 3, TimeSpan.FromMinutes(1)).PartitionKey);

    [Fact]
    public void TheLimit_HasADefault_AndCanBeTunedPerEnvironment()
    {
        var none = new ConfigurationBuilder().Build();
        PlatformRateLimiting.PermitLimit(none).Should().Be(PlatformRateLimiting.DefaultPermitLimit);
        PlatformRateLimiting.Window(none).Should().Be(PlatformRateLimiting.DefaultWindow);

        var tuned = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:platform:PermitLimit"] = "7",
            ["RateLimiting:platform:WindowSeconds"] = "30",
        }).Build();
        PlatformRateLimiting.PermitLimit(tuned).Should().Be(7);
        PlatformRateLimiting.Window(tuned).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ARefusedRequest_Gets429_WithRetryAfter()
    {
        var context = Request("10.0.0.1");
        var partition = PlatformRateLimiting.PartitionFor(context, 1, TimeSpan.FromMinutes(1));
        using var limiter = partition.Factory(partition.PartitionKey);
        limiter.AttemptAcquire();
        using var refused = limiter.AttemptAcquire();

        await PlatformRateLimiting.WriteRejectionAsync(context, refused, CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        int.Parse(context.Response.Headers.RetryAfter!).Should().BeGreaterThan(0);
    }
}
