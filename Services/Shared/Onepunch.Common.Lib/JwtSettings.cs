
namespace Onepunch.Common.Lib;

public class JwtSettings
{
    public string Issuer { get; set; }
    public List<string> Audience { get; set; }
    public string SigningKey { get; set; }
    public int TokenExpiry { get; set; } = 5;
    public int RefreshExpiry { get; set; } = 30;
    public string SigningKeyPath { get; set; } = "/app/signing-keys/auth-signing.key";

    /// <summary>
    /// Transition-window flag for the HMAC-to-RSA/JWKS migration: when true, validators also
    /// accept tokens signed with the legacy shared <see cref="SigningKey"/> secret alongside
    /// the new JWKS-resolved RSA key, so refresh tokens minted before cutover keep working.
    /// Remove once no legacy tokens remain in circulation (see RefreshExpiry).
    /// </summary>
    public bool AllowLegacyHmacValidation { get; set; } = true;
}
