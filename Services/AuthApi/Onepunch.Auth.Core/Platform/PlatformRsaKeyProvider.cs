using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Auth.Core.Services;

namespace Onepunch.Auth.Core.Platform;

/// <summary>
/// The signing keypair for PLATFORM operators' tokens. A different file and a different Data
/// Protection purpose from <see cref="RsaKeyProvider"/>, so compromising or rotating one identity's
/// key never touches the other, and a tenant token can't validate against it. Published at
/// <c>PlatformAuthDefaults.JwksPath</c>.
/// </summary>
public class PlatformRsaKeyProvider
{
    private readonly PersistedRsaKey _key;

    public RsaSecurityKey SigningKey => _key.SigningKey;
    public string Kid => _key.Kid;

    public PlatformRsaKeyProvider(IConfiguration configuration, IDataProtectionProvider dataProtectionProvider)
    {
        var path = configuration["PlatformAuth:SigningKeyPath"] ?? "/app/signing-keys/platform-signing.key";
        var protector = dataProtectionProvider.CreateProtector("OnePunch.Auth.PlatformJwtSigningKey");
        _key = new PersistedRsaKey(path, protector);
    }

    public JsonWebKey GetPublicJsonWebKey() => _key.GetPublicJsonWebKey();
}
