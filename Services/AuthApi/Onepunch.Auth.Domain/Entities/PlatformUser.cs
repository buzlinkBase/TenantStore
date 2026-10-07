namespace Onepunch.Auth.Domain.Entities;

/// <summary>
/// An operator of the Platform Admin Console. Deliberately NOT a tenant <c>User</c>: its own table,
/// its own credentials and token audience, so operators and customers can never be confused (the
/// tenant user table has a globally-unique email and a hardcoded audience). Lives in
/// PlatformAuthContext. Not an ASP.NET Identity user either -- Identity's options (password rules,
/// lockout) are process-wide and would have to be shared with the tenant users, so the few rules
/// that matter here are owned by PlatformAuthService/PlatformPasswordPolicy instead.
/// </summary>
public class PlatformUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    /// <summary>Upper-cased <see cref="Email"/>, the unique lookup key.</summary>
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>One of <c>PlatformRoles</c>; the permissions come from <c>PlatformRolePermissions</c>.</summary>
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Set for accounts created by an admin (or the bootstrap): the first sign-in must pick a new password.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>The TOTP secret, encrypted with Data Protection. Null until enrolment starts.</summary>
    public string? MfaSecretProtected { get; set; }
    /// <summary>Set once the user proved they hold the authenticator; MFA is mandatory, so no code check is skipped after this.</summary>
    public DateTime? MfaEnrolledAtUtc { get; set; }
    /// <summary>The last 30-second TOTP step accepted, so one code can never be used twice (replay).</summary>
    public long? LastUsedTotpStep { get; set; }

    public int FailedAccessCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    /// <summary>Rotated on password change / deactivation; embedded in nothing yet, kept for future session invalidation.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}

/// <summary>A platform session. Same single-use + rotation pattern as the tenant users' RefreshToken.</summary>
public class PlatformRefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }
    /// <summary>The token that replaced this one when it was rotated; a revoked token being presented again means it was stolen.</summary>
    public Guid? ReplacedByTokenId { get; set; }
}
