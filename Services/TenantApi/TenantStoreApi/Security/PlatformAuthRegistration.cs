using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Common.Lib.Platform;
using Onepunch.Common.Lib.Security;

namespace TenantStoreApi.Security;

/// <summary>The platform operators' signing keys, fetched from AuthApi's platform JWKS (a different key from the company users').</summary>
public sealed record PlatformJwksKeys(JwksKeyCache Cache);

/// <summary>
/// Requires a platform operator holding <paramref name="permission"/>. Authenticates ONLY with the
/// platform scheme, so a company user's token is rejected before the permission is even looked at,
/// and a platform token never reaches company endpoints (which use the default scheme).
/// </summary>
public sealed class PlatformAuthorizeAttribute : AuthorizeAttribute
{
    public PlatformAuthorizeAttribute(string permission)
    {
        Policy = permission;
        AuthenticationSchemes = PlatformAuthDefaults.Scheme;
    }
}

public static class PlatformAuthRegistration
{
    /// <summary>
    /// Registers the "Platform" authentication scheme (RS256 against AuthApi's platform JWKS) and one
    /// authorization policy per platform permission. Fallback policy and the company-user scheme are
    /// left exactly as they were.
    /// </summary>
    public static IServiceCollection AddPlatformAuth(this IServiceCollection services, string authUrl)
    {
        services.AddHttpClient(nameof(JwksKeyCache));
        // In-process only (no shared Redis copy): operator traffic is tiny, and the shared store has
        // one fixed key that belongs to the company users' keys.
        services.TryAddSingleton(sp => new PlatformJwksKeys(new JwksKeyCache(
            sp.GetRequiredService<IHttpClientFactory>(), new NullJwksStore(), authUrl,
            jwksPath: PlatformAuthDefaults.JwksPath)));

        services.AddAuthentication()
            .AddJwtBearer(PlatformAuthDefaults.Scheme, options => options.MapInboundClaims = false);
        services.AddOptions<JwtBearerOptions>(PlatformAuthDefaults.Scheme)
            .Configure<PlatformJwksKeys>((options, keys) =>
                options.TokenValidationParameters = CreateValidationParameters(keys.Cache));

        var authorization = services.AddAuthorizationBuilder();
        foreach (var permission in PlatformPermissions.All)
        {
            authorization.AddPolicy(permission, policy => policy
                .AddAuthenticationSchemes(PlatformAuthDefaults.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(PlatformAuthDefaults.PermissionClaim, permission));
        }
        return services;
    }

    /// <summary>How platform access tokens are validated here. Public so it can be tested without a web host.</summary>
    public static TokenValidationParameters CreateValidationParameters(JwksKeyCache keys) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = PlatformAuthDefaults.Issuer,
        ValidateAudience = true,
        ValidAudience = PlatformAuthDefaults.Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        IssuerSigningKeyResolver = (token, securityToken, kid, parameters) => keys.GetKeys(),
        NameClaimType = PlatformAuthDefaults.NameClaim,
        RoleClaimType = PlatformAuthDefaults.RoleClaim,
    };
}
