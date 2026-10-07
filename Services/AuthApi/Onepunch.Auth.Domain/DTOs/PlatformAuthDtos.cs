namespace Onepunch.Auth.Domain.DTOs;

// Wire contracts of the platform operators' sign-in (api/v1/platform/auth). The Platform Admin
// Console's server side is the only client: refresh tokens travel in the body, not in a cookie.

public record PlatformLoginRequest(string Email, string Password);

public static class PlatformLoginSteps
{
    /// <summary>First sign-in: the operator must scan a QR code and prove the authenticator works.</summary>
    public const string MfaSetup = "MfaSetup";
    /// <summary>Authenticator already enrolled: ask for the current code.</summary>
    public const string MfaVerify = "MfaVerify";
}

/// <summary>Result of a correct password. MFA is mandatory, so this is never a session by itself.</summary>
public record PlatformLoginChallenge(string Step, string ChallengeToken, DateTime ExpiresAtUtc, bool MustChangePassword);

public record PlatformMfaSetupRequest(string ChallengeToken);

public record PlatformMfaSetup(string Secret, string OtpAuthUri);

public record PlatformMfaVerifyRequest(string ChallengeToken, string Code);

public record PlatformRefreshRequest(string RefreshToken);

public record PlatformChangePasswordRequest(string CurrentPassword, string NewPassword);

public record PlatformProfile(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

public record PlatformSession(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    PlatformProfile User);

// ── Operator management (api/v1/platform/admins; SuperAdmin only) ───────────

public record PlatformAdminView(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsActive,
    bool MfaEnrolled,
    bool MustChangePassword,
    bool IsLocked,
    DateTime? LastLoginAtUtc,
    DateTime CreatedAtUtc,
    string? CreatedBy);

public record CreatePlatformAdminRequest(string Email, string DisplayName, string Role);

public record ChangePlatformAdminRoleRequest(string Role);

/// <summary>An operator plus the one-time password to hand them. Shown once; only its hash is stored.</summary>
public record PlatformAdminCredentials(PlatformAdminView Admin, string TemporaryPassword);
