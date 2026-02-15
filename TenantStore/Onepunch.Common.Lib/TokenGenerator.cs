using Onepunch.Common.Lib.DTO;
using System.Security.Cryptography;
using System.Text;

namespace Onepunch.Common.Lib;

public class TokenGenerator
{
    public static string GenerateRandomToken()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[16];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
    public static string Generate(Guid tenantId, string email)
    {
        using var rng = RandomNumberGenerator.Create();
        var rndToken = GenerateRandomToken();
        var eti = new EmailTokenInfo
        {
            Email = email,
            TenantId = tenantId,
            Token = rndToken
        };
        var token = ObjectSerializer.Serialize(eti);
        return TokenEncodingHelper.ToBase64Url(Encoding.UTF8.GetBytes(token));
    }
}

public class TokenEncodingHelper
{
    public static string ToBase64Url(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    public static byte[] FromBase64Url(string input)
    {
        try
        {
            string base64 = input.Replace("-", "+").Replace("_", "/");
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
        catch (Exception)
        {
            return Array.Empty<byte>();
        }
    }
}