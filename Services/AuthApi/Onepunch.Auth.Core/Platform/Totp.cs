using System.Security.Cryptography;
using System.Text;

namespace Onepunch.Auth.Core.Platform;

/// <summary>
/// Time-based one-time passwords (RFC 6238, HMAC-SHA1, 6 digits, 30-second steps) -- what every
/// authenticator app (Google Authenticator, Microsoft Authenticator, 1Password, ...) speaks. Pure
/// functions of (secret, time), so it is fully testable with the RFC's own vectors.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;
    private const int SecretBytes = 20; // 160 bits, the size RFC 4226 recommends

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    /// <summary>The 30-second step number a moment falls in.</summary>
    public static long StepOf(DateTimeOffset moment) => moment.ToUnixTimeSeconds() / StepSeconds;

    public static string ComputeCode(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(step & 0xFF);
            step >>= 8;
        }

        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];
        var code = binary % (int)Math.Pow(10, Digits);
        return code.ToString().PadLeft(Digits, '0');
    }

    /// <summary>
    /// Checks <paramref name="code"/> against the current step and <paramref name="window"/> steps
    /// either side (clock drift). Returns the step that matched, or null. A step at or before
    /// <paramref name="lastUsedStep"/> never matches, so a code (including one shoulder-surfed or
    /// intercepted) can be used once.
    /// </summary>
    public static long? Verify(byte[] secret, string? code, DateTimeOffset now, long? lastUsedStep, int window = 1)
    {
        var digits = (code ?? string.Empty).Replace(" ", "").Replace("-", "");
        if (digits.Length != Digits || !digits.All(char.IsAsciiDigit)) return null;

        var current = StepOf(now);
        for (var step = current - window; step <= current + window; step++)
        {
            if (lastUsedStep.HasValue && step <= lastUsedStep.Value) continue;
            var expected = Encoding.ASCII.GetBytes(ComputeCode(secret, step));
            if (CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(digits))) return step;
        }
        return null;
    }

    /// <summary>The otpauth:// URI authenticator apps import (usually shown as a QR code).</summary>
    public static string BuildOtpAuthUri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={Base32Encode(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Base32Encode(byte[] data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(Alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0) output.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return output.ToString();
    }
}
