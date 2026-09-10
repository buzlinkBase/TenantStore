using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

namespace Onepunch.Auth.Core.Services;

/// <summary>
/// Owns the Auth Service's RSA signing keypair: loads it from disk (encrypted at rest via
/// ASP.NET Data Protection, reusing the same key-ring convention as the existing
/// PersistKeysToFileSystem(@"/app/dp-keys") setup) or generates and persists one on first run.
/// Registered as a singleton so the keypair is created/loaded exactly once per process.
/// </summary>
public class RsaKeyProvider
{
    private readonly RSA _rsa;

    public RsaSecurityKey SigningKey { get; }
    public string Kid { get; }

    public RsaKeyProvider(IConfiguration configuration, IDataProtectionProvider dataProtectionProvider)
    {
        var path = configuration["JwtSettings:SigningKeyPath"] ?? "/app/signing-keys/auth-signing.key";
        var protector = dataProtectionProvider.CreateProtector("OnePunch.Auth.JwtSigningKey");
        _rsa = LoadOrCreate(path, protector);
        Kid = ComputeKid(_rsa);
        SigningKey = new RsaSecurityKey(_rsa) { KeyId = Kid };
    }

    /// <summary>Public-only JWK for the /.well-known/jwks.json endpoint.</summary>
    public JsonWebKey GetPublicJsonWebKey()
    {
        using var publicOnly = RSA.Create();
        publicOnly.ImportParameters(_rsa.ExportParameters(false));
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(publicOnly));
        jwk.Kid = Kid;
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        return jwk;
    }

    private static RSA LoadOrCreate(string path, IDataProtector protector)
    {
        try
        {
            if (File.Exists(path))
            {
                var protectedPayload = Encoding.UTF8.GetString(File.ReadAllBytes(path));
                var xml = protector.Unprotect(protectedPayload);
                var rsa = RSA.Create();
                rsa.FromXmlString(xml);
                return rsa;
            }
        }
        catch
        {
            // Persisted key is missing/corrupt/undecryptable (e.g. Data Protection key ring
            // rotated) — fall through and mint a fresh keypair rather than crash on startup.
        }

        var generated = RSA.Create(2048);
        Persist(path, protector, generated);
        return generated;
    }

    private static void Persist(string path, IDataProtector protector, RSA rsa)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var xml = rsa.ToXmlString(true);
        var protectedPayload = protector.Protect(xml);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(protectedPayload));
    }

    private static string ComputeKid(RSA rsa)
    {
        var modulus = rsa.ExportParameters(false).Modulus!;
        var hash = SHA256.HashData(modulus);
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
