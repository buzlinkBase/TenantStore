using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using TenantStoreApi.Extensions;
using Xunit;

namespace TenantStoreApi.Tests;

/// <summary>Which proxies may set X-Forwarded-For, since client IPs key the rate limits.</summary>
public class ForwardedHeadersTrustTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void NothingConfigured_TrustsWhicheverProxyConnects()
    {
        var options = new ForwardedHeadersOptions().Apply(Config());

        options.KnownProxies.Should().BeEmpty();
        options.KnownNetworks.Should().BeEmpty();
    }

    [Fact]
    public void ListedProxiesAndNetworks_AreTheOnlyOnesTrusted()
    {
        var options = new ForwardedHeadersOptions().Apply(Config(
            ("ForwardedHeaders:KnownProxies:0", "10.0.0.5"),
            ("ForwardedHeaders:KnownNetworks:0", "172.16.0.0/12")));

        options.KnownProxies.Should().ContainSingle().Which.Should().Be(IPAddress.Parse("10.0.0.5"));
        options.KnownNetworks.Should().ContainSingle();
        options.KnownNetworks[0].Contains(IPAddress.Parse("172.20.1.1")).Should().BeTrue();
        options.KnownNetworks[0].Contains(IPAddress.Parse("10.1.1.1")).Should().BeFalse();
    }

    [Fact]
    public void TheDefaultLoopbackTrust_IsReplaced_NotAddedTo()
    {
        var options = new ForwardedHeadersOptions().Apply(Config(("ForwardedHeaders:KnownProxies:0", "10.0.0.5")));

        options.KnownProxies.Should().NotContain(IPAddress.Loopback);
        options.KnownProxies.Should().NotContain(IPAddress.IPv6Loopback);
    }

    [Fact]
    public void AMalformedNetwork_FailsLoudly_AtStartup()
    {
        var act = () => new ForwardedHeadersOptions().Apply(Config(("ForwardedHeaders:KnownNetworks:0", "172.16.0.0")));

        act.Should().Throw<FormatException>();
    }
}
