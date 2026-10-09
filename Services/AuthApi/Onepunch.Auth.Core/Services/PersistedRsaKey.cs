using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

namespace Onepunch.Auth.Core.Services;

/// <summary>
/// An RSA signing keypair persisted on disk, encrypted at rest with ASP.NET Data Protection:
/// loaded if present, otherwise generated and saved on first use. The one implementation behind
/// both signing identities this service has -- tenant users (<see cref="RsaKeyProvider"/>) and
/// platform operators (<see cref="PlatformRsaKeyProvider"/>) -- which differ only in file path and
/// protector purpose, so they can never share or leak a key.
/// </summary>
public sealed class PersistedRsaKey
{
    private readonly RSA _rsa;

    public RsaSecurityKey SigningKey { get; }
    public string Kid { get; }

    public PersistedRsaKey(string path, IDataProtector protector)
    {
        _rsa = LoadOrCreate(path, protector);
        Kid = ComputeKid(_rsa);
        SigningKey = new RsaSecurityKey(_rsa) { KeyId = Kid };
    }

    /// <summary>Public-only JWK for a JWKS endpoint.</summary>
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
