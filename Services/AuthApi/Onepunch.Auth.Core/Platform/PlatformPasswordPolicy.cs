namespace Onepunch.Auth.Core.Platform;

/// <summary>
/// Password rules for platform operators -- stricter than the tenant users' (6 characters), since
/// these accounts can change any customer's billing. Owned here rather than in ASP.NET Identity's
/// options, which are process-wide and shared with the tenant users.
/// </summary>
public static class PlatformPasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    /// <summary>The reasons <paramref name="password"/> is unacceptable; empty when it is fine.</summary>
    public static IReadOnlyList<string> Validate(string? password, string? email)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required.");
            return errors;
        }

        if (password.Length < MinLength) errors.Add($"Password must be at least {MinLength} characters.");
        if (password.Length > MaxLength) errors.Add($"Password must be at most {MaxLength} characters.");
        if (!password.Any(char.IsUpper)) errors.Add("Password needs an uppercase letter.");
        if (!password.Any(char.IsLower)) errors.Add("Password needs a lowercase letter.");
        if (!password.Any(char.IsDigit)) errors.Add("Password needs a digit.");
        if (password.All(char.IsLetterOrDigit)) errors.Add("Password needs a symbol.");

        var localPart = email?.Split('@')[0];
        if (localPart is { Length: >= 4 } && password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
            errors.Add("Password must not contain your email name.");

        return errors;
    }
}
