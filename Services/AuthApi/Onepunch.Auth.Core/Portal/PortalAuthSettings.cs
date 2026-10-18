using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

namespace Onepunch.Auth.Core.Portal;

/// <summary>Settings of the Client Portal's sign-in (configuration section <c>PortalAuth</c>).</summary>
public class PortalAuthSettings
{
    public const string SectionName = "PortalAuth";

    /// <summary>Header carrying the shared secret; every portal endpoint requires it.</summary>
    public const string KeyHeader = "X-Portal-Key";

    /// <summary>Header carrying the end user's address, so rate limits count users rather than the portal's one server.</summary>
    public const string ClientIpHeader = "X-Portal-Client-Ip";

    /// <summary>
    /// The audience of portal access tokens. Every other service accepts only the HRIS audience, so a
    /// portal token is useless anywhere but the portal; the portal accepts only this one.
    /// </summary>
    public string Audience { get; set; } = "Onepunch.ClientPortal";

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 7;

    /// <summary>
    /// A shared secret the Client Portal's server presents (header <c>X-Portal-Key</c>) on every call. Only that server
    /// calls these endpoints, so without it nothing here answers. Empty switches them all off.
    /// </summary>
    public string ServiceKey { get; set; } = string.Empty;

    /// <summary>The portal's public address; password reset links point at <c>{PortalUrl}/reset-password</c>.</summary>
    public string PortalUrl { get; set; } = string.Empty;

    /// <summary>Name shown in the password reset email.</summary>
    public string AppName { get; set; } = "Client Portal";
}

public static class PortalTokenValidation
{
    /// <summary>How the portal audience's tokens are validated (the key is the same one that signs HRIS tokens).</summary>
    public static TokenValidationParameters AccessToken(SecurityKey signingKey, string issuer, string audience) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
    };

    /// <summary>Constant-time comparison of the presented service key.</summary>
    public static bool KeyMatches(string? presented, string expected)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(presented)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
