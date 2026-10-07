using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Exceptions;
using Onepunch.Common.Lib.Platform;

namespace Onepunch.Auth.Core.Platform;

/// <summary>
/// Sign-in for the Platform Admin Console's operators: password, then a mandatory authenticator
/// code (TOTP), then a short access token plus a single-use rotating refresh token.
///
/// Failure messages are deliberately generic ("Invalid credentials"): they never say whether the
/// email exists, is locked or is deactivated -- that goes to the log only. Password and code
/// failures share one lockout counter, so the 6-digit code can't be brute-forced.
/// </summary>
public class PlatformAuthService
{
    private const string InvalidCredentials = "Invalid credentials.";
    private const string InvalidCode = "Invalid code.";
    private const string InvalidSession = "Session is no longer valid. Please sign in again.";

    private readonly PlatformAuthContext _db;
    private readonly PlatformTokenService _tokens;
    private readonly IDataProtector _secretProtector;
    private readonly PlatformAuthSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<PlatformAuthService> _logger;
    private readonly PasswordHasher<PlatformUser> _hasher = new();

    // Verified against when the email is unknown, so a miss costs as long as a hit.
    private static readonly PlatformUser DummyUser = new();
    private readonly string _dummyHash;

    public PlatformAuthService(
        PlatformAuthContext db,
        PlatformTokenService tokens,
        IDataProtectionProvider dataProtection,
        IOptions<PlatformAuthSettings> settings,
        TimeProvider time,
        ILogger<PlatformAuthService> logger)
    {
        _db = db;
        _tokens = tokens;
        _secretProtector = dataProtection.CreateProtector("OnePunch.Auth.PlatformMfaSecret");
        _settings = settings.Value;
        _time = time;
        _logger = logger;
        _dummyHash = _hasher.HashPassword(DummyUser, Guid.NewGuid().ToString("N"));
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToUpperInvariant();

    // ── Sign-in ──────────────────────────────────────────────────────────────

    public async Task<PlatformLoginChallenge> LoginAsync(string? email, string? password, CancellationToken ct)
    {
        var normalized = NormalizeEmail(email);
        var user = normalized.Length == 0
            ? null
            : await _db.PlatformUsers.FirstOrDefaultAsync(x => x.NormalizedEmail == normalized, ct);

        if (user is null)
        {
            _hasher.VerifyHashedPassword(DummyUser, _dummyHash, password ?? string.Empty);
            _logger.LogWarning("Platform sign-in rejected: unknown email");
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!user.IsActive || IsLocked(user))
        {
            _hasher.VerifyHashedPassword(DummyUser, _dummyHash, password ?? string.Empty);
            _logger.LogWarning("Platform sign-in rejected for {UserId}: {Reason}", user.Id, user.IsActive ? "locked" : "deactivated");
            throw new UnauthorizedException(InvalidCredentials);
        }

        var verdict = _hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        if (verdict == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(user, "wrong password", ct);
            throw new UnauthorizedException(InvalidCredentials);
        }
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password!);
            await _db.SaveChangesAsync(ct);
        }

        var enrolled = user.MfaEnrolledAtUtc.HasValue;
        var (token, expires) = _tokens.CreateChallengeToken(user);
        return new PlatformLoginChallenge(
            enrolled ? PlatformLoginSteps.MfaVerify : PlatformLoginSteps.MfaSetup,
            token,
            expires,
            user.MustChangePassword);
    }

    /// <summary>First-time enrolment: the secret to put in the authenticator app. Stable across calls until the code is confirmed.</summary>
    public async Task<PlatformMfaSetup> GetMfaSetupAsync(string? challengeToken, CancellationToken ct)
    {
        var user = await ResolveChallengeUserAsync(challengeToken, ct);
        if (user.MfaEnrolledAtUtc.HasValue)
            throw new GuardException("Authenticator is already set up for this account.");

        byte[] secret;
        if (user.MfaSecretProtected is null)
        {
            secret = Totp.NewSecret();
            user.MfaSecretProtected = _secretProtector.Protect(Convert.ToBase64String(secret));
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            secret = Convert.FromBase64String(_secretProtector.Unprotect(user.MfaSecretProtected));
        }

        return new PlatformMfaSetup(
            Totp.Base32Encode(secret),
            Totp.BuildOtpAuthUri(_settings.TotpIssuer, user.Email, secret));
    }

    /// <summary>Completes sign-in (or enrolment) with the authenticator's current code and opens the session.</summary>
    public async Task<PlatformSession> VerifyMfaAsync(string? challengeToken, string? code, CancellationToken ct)
    {
        var user = await ResolveChallengeUserAsync(challengeToken, ct);
        if (user.MfaSecretProtected is null)
            throw new GuardException("Set up your authenticator first.");

        var secret = Convert.FromBase64String(_secretProtector.Unprotect(user.MfaSecretProtected));
        var step = Totp.Verify(secret, code, _time.GetUtcNow(), user.LastUsedTotpStep);
        if (step is null)
        {
            await RegisterFailureAsync(user, "wrong code", ct);
            throw new UnauthorizedException(InvalidCode);
        }

        user.LastUsedTotpStep = step;
        user.MfaEnrolledAtUtc ??= Now;
        user.FailedAccessCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = Now;
        _logger.LogInformation("Platform sign-in completed for {UserId}", user.Id);
        return await OpenSessionAsync(user, ct);
    }

    // ── Session ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Single-use rotation: the presented token is revoked and replaced. A token that was already
    /// revoked and shows up again was copied, so every session of that operator is ended.
    /// </summary>
    public async Task<PlatformSession> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new UnauthorizedException(InvalidSession);

        var hash = PlatformTokenService.HashRefreshToken(refreshToken);
        var stored = await _db.PlatformRefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null) throw new UnauthorizedException(InvalidSession);

        if (stored.RevokedAtUtc.HasValue)
        {
            var justRotated = stored.RevokedReason == "rotated"
                && Now - stored.RevokedAtUtc.Value <= TimeSpan.FromSeconds(_settings.RefreshReuseGraceSeconds);
            if (!justRotated)
            {
                _logger.LogWarning("Revoked platform refresh token reused for {UserId}; ending all sessions", stored.UserId);
                await RevokeAllAsync(stored.UserId, "reuse-detected", ct);
            }
            throw new UnauthorizedException(InvalidSession);
        }
        if (stored.ExpiresAtUtc <= Now) throw new UnauthorizedException(InvalidSession);

        var user = await _db.PlatformUsers.FirstOrDefaultAsync(x => x.Id == stored.UserId, ct);
        if (user is null || !user.IsActive || IsLocked(user)) throw new UnauthorizedException(InvalidSession);

        var session = await OpenSessionAsync(user, ct, replacing: stored);
        return session;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = PlatformTokenService.HashRefreshToken(refreshToken);
        var stored = await _db.PlatformRefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null || stored.RevokedAtUtc.HasValue) return;
        stored.RevokedAtUtc = Now;
        stored.RevokedReason = "logout";
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PlatformProfile> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || !user.IsActive) throw new UnauthorizedException(InvalidSession);
        return ToProfile(user);
    }

    public async Task<PlatformSession> ChangePasswordAsync(Guid userId, PlatformChangePasswordRequest request, CancellationToken ct)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || !user.IsActive || IsLocked(user)) throw new UnauthorizedException(InvalidSession);

        var verdict = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? string.Empty);
        if (verdict == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(user, "wrong current password", ct);
            throw new UnauthorizedException(InvalidCredentials);
        }

        var errors = PlatformPasswordPolicy.Validate(request.NewPassword, user.Email);
        if (errors.Count > 0) throw new GuardException(string.Join(" ", errors));
        if (request.NewPassword == request.CurrentPassword)
            throw new GuardException("The new password must be different from the current one.");

        user.PasswordHash = _hasher.HashPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedAccessCount = 0;
        await RevokeAllAsync(user.Id, "password-changed", ct, save: false);
        _logger.LogInformation("Platform password changed for {UserId}", user.Id);
        return await OpenSessionAsync(user, ct);
    }

    // ── Account creation (bootstrap now, admin management later) ─────────────

    /// <summary>
    /// Creates an operator with a temporary password. They must change it, and enrol an authenticator,
    /// at first sign-in. <paramref name="createdBy"/> is whoever did it (an operator's email, or "bootstrap").
    /// </summary>
    public async Task<PlatformUser> CreateUserAsync(
        string email, string displayName, string role, string temporaryPassword, string createdBy, CancellationToken ct)
    {
        if (!PlatformRoles.IsKnown(role)) throw new GuardException($"Unknown platform role '{role}'.");
        var normalized = NormalizeEmail(email);
        if (normalized.Length == 0 || !normalized.Contains('@')) throw new GuardException("A valid email is required.");
        if (string.IsNullOrWhiteSpace(displayName)) throw new GuardException("A display name is required.");
        var errors = PlatformPasswordPolicy.Validate(temporaryPassword, email);
        if (errors.Count > 0) throw new GuardException(string.Join(" ", errors));
        if (await _db.PlatformUsers.AnyAsync(x => x.NormalizedEmail == normalized, ct))
            throw new GuardException("A platform user with this email already exists.");

        var user = new PlatformUser
        {
            Email = email.Trim(),
            NormalizedEmail = normalized,
            DisplayName = displayName.Trim(),
            Role = role,
            MustChangePassword = true,
            CreatedAtUtc = Now,
            CreatedBy = createdBy,
        };
        user.PasswordHash = _hasher.HashPassword(user, temporaryPassword);
        _db.PlatformUsers.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task<PlatformUser> ResolveChallengeUserAsync(string? challengeToken, CancellationToken ct)
    {
        var userId = _tokens.ReadChallengeToken(challengeToken);
        if (userId is null) throw new UnauthorizedException(InvalidSession);

        var user = await _db.PlatformUsers.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || !user.IsActive || IsLocked(user)) throw new UnauthorizedException(InvalidSession);
        return user;
    }

    public bool IsLocked(PlatformUser user) => user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > Now;

    private async Task RegisterFailureAsync(PlatformUser user, string reason, CancellationToken ct)
    {
        user.FailedAccessCount++;
        if (user.FailedAccessCount >= _settings.MaxFailedAccessAttempts)
        {
            user.LockoutEndUtc = Now.AddMinutes(_settings.LockoutMinutes);
            user.FailedAccessCount = 0;
            _logger.LogWarning("Platform user {UserId} locked until {Until} after repeated failures", user.Id, user.LockoutEndUtc);
        }
        else
        {
            _logger.LogWarning("Platform sign-in failure for {UserId}: {Reason}", user.Id, reason);
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task<PlatformSession> OpenSessionAsync(PlatformUser user, CancellationToken ct, PlatformRefreshToken? replacing = null)
    {
        var refresh = PlatformTokenService.NewRefreshToken();
        var entry = new PlatformRefreshToken
        {
            UserId = user.Id,
            TokenHash = PlatformTokenService.HashRefreshToken(refresh),
            CreatedAtUtc = Now,
            ExpiresAtUtc = Now.AddDays(_settings.RefreshDays),
        };
        _db.PlatformRefreshTokens.Add(entry);

        if (replacing is not null)
        {
            replacing.RevokedAtUtc = Now;
            replacing.RevokedReason = "rotated";
            replacing.ReplacedByTokenId = entry.Id;
        }
        await _db.SaveChangesAsync(ct);

        var (access, accessExpires) = _tokens.CreateAccessToken(user);
        return new PlatformSession(access, accessExpires, refresh, entry.ExpiresAtUtc, ToProfile(user));
    }

    /// <summary>Ends every session of an operator (their refresh tokens); a live access token still runs out within minutes.</summary>
    public async Task RevokeAllAsync(Guid userId, string reason, CancellationToken ct, bool save = true)
    {
        var active = await _db.PlatformRefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in active)
        {
            token.RevokedAtUtc = Now;
            token.RevokedReason = reason;
        }
        if (save) await _db.SaveChangesAsync(ct);
    }

    private static PlatformProfile ToProfile(PlatformUser user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        user.MustChangePassword ? [] : PlatformRolePermissions.For(user.Role),
        user.MustChangePassword);
}
