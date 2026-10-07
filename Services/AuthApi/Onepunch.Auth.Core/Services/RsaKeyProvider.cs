using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Onepunch.Auth.Core.Services;

/// <summary>
/// Owns the Auth Service's RSA signing keypair for TENANT users: loads it from disk (encrypted at
/// rest via ASP.NET Data Protection, reusing the same key-ring convention as the existing
/// PersistKeysToFileSystem(@"/app/dp-keys") setup) or generates and persists one on first run.
/// Registered as a singleton so the keypair is created/loaded exactly once per process.
/// Platform operators sign with their own key -- see <see cref="PlatformRsaKeyProvider"/>.
/// </summary>
public class RsaKeyProvider
{
    private readonly PersistedRsaKey _key;

    public RsaSecurityKey SigningKey => _key.SigningKey;
    public string Kid => _key.Kid;

    public RsaKeyProvider(IConfiguration configuration, IDataProtectionProvider dataProtectionProvider)
    {
        var path = configuration["JwtSettings:SigningKeyPath"] ?? "/app/signing-keys/auth-signing.key";
        var protector = dataProtectionProvider.CreateProtector("OnePunch.Auth.JwtSigningKey");
        _key = new PersistedRsaKey(path, protector);
    }

    /// <summary>Public-only JWK for the /.well-known/jwks.json endpoint.</summary>
    public JsonWebKey GetPublicJsonWebKey() => _key.GetPublicJsonWebKey();
}
