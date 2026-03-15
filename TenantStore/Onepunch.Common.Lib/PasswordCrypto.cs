using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Onepunch.Common.Lib; 

public class PasswordCrypto
{
    private readonly byte[] _aesKey;
    private readonly byte[] _aesIV;
    public PasswordCrypto(IOptions<CryptoSetting> options)
    {
        var crypto = options.Value;
        _aesKey = Convert.FromBase64String(crypto.AES_KEY);
        _aesIV = Convert.FromBase64String(crypto.AES_IV);
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentNullException(nameof(plainText));

        using var aes = Aes.Create();
        aes.Key = _aesKey;
        aes.IV = _aesIV;

        using var encryptor = aes.CreateEncryptor();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return Convert.ToBase64String(cipherBytes);
    }

    public string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            throw new ArgumentNullException(nameof(encryptedText));

        byte[] cipherBytes = Convert.FromBase64String(encryptedText);

        using var aes = Aes.Create();
        aes.Key = _aesKey;
        aes.IV = _aesIV;

        using var decryptor = aes.CreateDecryptor();
        byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
