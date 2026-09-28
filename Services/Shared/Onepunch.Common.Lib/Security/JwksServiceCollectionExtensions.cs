using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;

namespace Onepunch.Common.Lib.Security;

public static class JwksServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="JwksKeyCache"/> for the Auth service's JWKS, shared through Redis when a
    /// connection string is given (an existing <see cref="IConnectionMultiplexer"/> registration is
    /// reused), otherwise per process via <see cref="NullJwksStore"/>.
    /// </summary>
    public static IServiceCollection AddJwksKeyCache(this IServiceCollection services, string authUrl, string? redisConnection)
    {
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
            services.TryAddSingleton<IJwksStore, RedisJwksStore>();
        }
        else
        {
            services.TryAddSingleton<IJwksStore, NullJwksStore>();
        }

        services.AddHttpClient(nameof(JwksKeyCache));
        services.TryAddSingleton(sp => new JwksKeyCache(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IJwksStore>(),
            authUrl));
        return services;
    }

    /// <summary>
    /// The signing-key resolver for JwtBearer: JWKS keys from <paramref name="cache"/>, plus the
    /// legacy HMAC key (created once, not per validation) while that fallback is allowed.
    /// </summary>
    public static IssuerSigningKeyResolver CreateSigningKeyResolver(JwksKeyCache cache, string? legacySigningKey, bool allowLegacyHmac)
    {
        SecurityKey? legacyKey = allowLegacyHmac && !string.IsNullOrEmpty(legacySigningKey)
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(legacySigningKey))
            : null;

        return (token, securityToken, kid, validationParameters) =>
        {
            var keys = cache.GetKeys();
            return legacyKey == null ? keys : [.. keys, legacyKey];
        };
    }
}
