namespace Onepunch.Auth.Core.Platform;

/// <summary>Configuration section <c>PlatformAuth</c>. Every value has a safe default.</summary>
public class PlatformAuthSettings
{
    public const string SectionName = "PlatformAuth";

    public int AccessTokenMinutes { get; set; } = 5;
    /// <summary>How long the password-then-code step may take.</summary>
    public int ChallengeMinutes { get; set; } = 5;
    public int RefreshDays { get; set; } = 7;
    public int MaxFailedAccessAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    /// <summary>A just-rotated refresh token presented again within this window (two tabs racing) is rejected without revoking the whole session.</summary>
    public int RefreshReuseGraceSeconds { get; set; } = 10;
    /// <summary>Shown in the authenticator app.</summary>
    public string TotpIssuer { get; set; } = "Onepunch Platform";

    public BootstrapSettings Bootstrap { get; set; } = new();

    /// <summary>
    /// Creates the FIRST SuperAdmin when the platform user table is empty (there is no self-registration).
    /// Set both values once, start the API, sign in (you must pick a new password and enrol MFA), then
    /// remove them from the environment. Ignored as soon as any platform user exists.
    /// </summary>
    public class BootstrapSettings
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? DisplayName { get; set; }
    }
}
