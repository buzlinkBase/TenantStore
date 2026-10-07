using System.Security.Cryptography;

namespace Onepunch.Auth.Core.Platform;

/// <summary>Makes the one-time passwords handed to a new or reset operator.</summary>
public interface IPlatformPasswordGenerator
{
    string Create(string email);
}

/// <summary>
/// Random, readable (no look-alike characters) and always accepted by <see cref="PlatformPasswordPolicy"/>
/// for the given email. Only ever a temporary password: the operator must replace it at first sign-in.
/// </summary>
public sealed class PlatformPasswordGenerator : IPlatformPasswordGenerator
{
    private const int Length = 18;
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+?";
    private const string All = Upper + Lower + Digits + Symbols;

    public string Create(string email)
    {
        while (true)
        {
            var characters = new List<char>(Length)
            {
                Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols),
            };
            while (characters.Count < Length) characters.Add(Pick(All));
            RandomNumberGenerator.Shuffle(CollectionsMarshalSpan(characters));

            var password = new string(characters.ToArray());
            // Vanishingly unlikely to contain the email name, but the policy is the authority.
            if (PlatformPasswordPolicy.Validate(password, email).Count == 0) return password;
        }
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static Span<char> CollectionsMarshalSpan(List<char> list) =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
}
