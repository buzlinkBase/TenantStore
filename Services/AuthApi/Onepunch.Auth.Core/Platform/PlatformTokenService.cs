using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib.Platform;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Onepunch.Auth.Core.Platform;

/// <summary>
/// How AuthApi's own "Platform" authentication scheme validates platform ACCESS tokens (for
/// /me and /change-password). A static function of the public signing key so the scheme can be
/// configured from singletons only -- PlatformTokenService itself is scoped. TenantApi validates the
/// same tokens through the published JWKS instead, with the same issuer/audience.
/// </summary>
public static class PlatformTokenValidation
{
    public static TokenValidationParameters AccessToken(SecurityKey signingKey) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = PlatformAuthDefaults.Issuer,
        ValidateAudience = true,
        ValidAudience = PlatformAuthDefaults.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        NameClaimType = PlatformAuthDefaults.NameClaim,
        RoleClaimType = PlatformAuthDefaults.RoleClaim,
    };
}

/// <summary>
/// Mints and reads platform operators' tokens. Two kinds, with different audiences so neither can
/// stand in for the other (or for a tenant user's token, which has another issuer, audience and key):
/// - the ACCESS token the platform API accepts (<see cref="PlatformAuthDefaults.Audience"/>);
/// - the CHALLENGE token issued after a correct password, good only to finish the MFA step.
/// </summary>
public class PlatformTokenService
{
    private readonly PlatformRsaKeyProvider _keys;
    private readonly PlatformAuthSettings _settings;
    private readonly TimeProvider _time;

    public PlatformTokenService(PlatformRsaKeyProvider keys, IOptions<PlatformAuthSettings> settings, TimeProvider time)
    {
        _keys = keys;
        _settings = settings.Value;
        _time = time;
    }

    /// <summary>
    /// An operator who still has to change their initial password gets a token with NO permission
    /// claims: it can identify them (/me, /change-password) but every policy on the platform API
    /// fails closed until the password is changed and a fresh token is issued.
    /// </summary>
    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(PlatformUser user)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(PlatformAuthDefaults.NameClaim, user.DisplayName),
            new(PlatformAuthDefaults.RoleClaim, user.Role),
            new("amr", "pwd"),
            new("amr", "otp"),
        };
        if (user.MustChangePassword)
        {
            claims.Add(new Claim("pwd_change", "required"));
        }
        else
        {
            claims.AddRange(PlatformRolePermissions.For(user.Role)
                .Select(p => new Claim(PlatformAuthDefaults.PermissionClaim, p)));
        }

        return (Write(claims, PlatformAuthDefaults.Audience, now, expires), expires);
    }

    public (string Token, DateTime ExpiresAtUtc) CreateChallengeToken(PlatformUser user)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_settings.ChallengeMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("purpose", "mfa"),
        };
        return (Write(claims, PlatformAuthDefaults.ChallengeAudience, now, expires), expires);
    }

    /// <summary>The user a challenge token was issued to, or null if it is forged, expired or not a challenge token.</summary>
    public Guid? ReadChallengeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = PlatformAuthDefaults.Issuer,
                ValidateAudience = true,
                ValidAudience = PlatformAuthDefaults.ChallengeAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _keys.SigningKey,
                // Lifetime is checked against the injected clock below, not the system clock.
                ValidateLifetime = false,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            }, out var validated);

            if (validated.ValidTo < _time.GetUtcNow().UtcDateTime) return null;
            if (principal.FindFirst("purpose")?.Value != "mfa") return null;
            return Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A fresh opaque refresh token (returned once; only its hash is stored).</summary>
    public static string NewRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string HashRefreshToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private string Write(IEnumerable<Claim> claims, string audience, DateTime now, DateTime expires)
    {
        var credentials = new SigningCredentials(_keys.SigningKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: PlatformAuthDefaults.Issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
