using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace TenantStoreApi.Extensions;

/// <summary>
/// Which proxies may set X-Forwarded-For/-Proto/-Host. Client IPs feed the rate limits, so a caller
/// who can reach the API directly must not be able to pick its own IP by sending the header.
/// Configure <c>ForwardedHeaders:KnownProxies</c> (addresses) and/or <c>ForwardedHeaders:KnownNetworks</c>
/// (CIDR, e.g. 172.16.0.0/12) with the reverse proxy's address. Left empty, every proxy is trusted
/// (the previous behaviour -- fine only while the API is reachable through the proxy alone).
/// </summary>
public static class ForwardedHeadersTrust
{
    public static ForwardedHeadersOptions Apply(this ForwardedHeadersOptions options, IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];

        // Cleared first: the defaults trust loopback only, which is not where a docker proxy connects from.
        // With nothing listed they stay empty, which trusts whichever proxy connects (as before).
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();
        foreach (var proxy in proxies.Where(p => !string.IsNullOrWhiteSpace(p)))
            options.KnownProxies.Add(IPAddress.Parse(proxy.Trim()));
        foreach (var network in networks.Where(n => !string.IsNullOrWhiteSpace(n)))
            options.KnownNetworks.Add(ParseNetwork(network.Trim()));
        return options;
    }

    private static Microsoft.AspNetCore.HttpOverrides.IPNetwork ParseNetwork(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix))
            throw new FormatException($"ForwardedHeaders:KnownNetworks entry '{cidr}' is not in address/prefix form.");
        return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse(parts[0]), prefix);
    }
}
