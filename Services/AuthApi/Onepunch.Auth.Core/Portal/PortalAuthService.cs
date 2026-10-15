using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Exceptions;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Onepunch.Auth.Core.Portal;

/// <summary>
/// Sign-in for the Client Portal's users: the same accounts, password rules, lockout and reset tokens as
/// the rest of the platform (ASP.NET Identity), but a session of its own. Its access token has the portal
/// audience, so no HRIS service accepts it, and its refresh token is tagged for the portal so the HRIS
/// refresh refuses it. Who the person is inside the portal (which client, which roles) is the portal's
/// business, not carried here: the token only says who they are.
///
/// Failure messages are deliberately generic; whether an email exists is never revealed.
/// </summary>
public class PortalAuthService : BaseService<RefreshToken>
{
    private const string BadCredentials = "Invalid email or password.";
    private const string Locked = "Account is temporarily locked. Please try again later.";
    private const string InvalidSession = "Session expired. Please sign in again.";

    /// <summary>The claim that marks a portal access token (alongside its audience).</summary>
    public const string UserTypeClaim = "userType";
    public const string UserTypeValue = "client-portal";

    private readonly UserManager<User> _manager;
    private readonly RsaKeyProvider _keys;
    private readonly JwtSettings _jwt;
    private readonly PortalAuthSettings _settings;
    private readonly IPublishEndpoint _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<PortalAuthService> _logger;

    public PortalAuthService(
        IUnitOfWorkService uow,
        UserManager<User> manager,
        RsaKeyProvider keys,
        IOptions<JwtSettings> jwt,
        IOptions<PortalAuthSettings> settings,
        IPublishEndpoint publisher,
        TimeProvider time,
        ILogger<PortalAuthService> logger) : base(uow)
    {
        _manager = manager;
        _keys = keys;
        _jwt = jwt.Value;
        _settings = settings.Value;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // ── Sign-in ──────────────────────────────────────────────────────────────

    public async Task<PortalSession> LoginAsync(string? email, string? password, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(email) ? null : await _manager.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            _logger.LogWarning("Portal sign-in rejected: unknown email");
            throw new UnauthorizedException(BadCredentials);
        }

        if (await _manager.IsLockedOutAsync(user))
            throw new UnauthorizedException(Locked);

        if (!await _manager.CheckPasswordAsync(user, password ?? string.Empty))
        {
            await _manager.AccessFailedAsync(user);
            // Persisted on its own so the failure counter (and so the lockout) survives the error.
            await CommitChangesAsync(ct);
            throw new UnauthorizedException(await _manager.IsLockedOutAsync(user) ? Locked : BadCredentials);
        }

        if (user.Status != "Active" || !user.EmailConfirmed)
            throw new UnauthorizedException(BadCredentials);

        await _manager.ResetAccessFailedCountAsync(user);
        return await OpenSessionAsync(user, replacing: null, ct);
    }

    /// <summary>Single-use rotation: the presented token is revoked and replaced.</summary>
    public async Task<PortalSession> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new UnauthorizedException(InvalidSession);

        var hash = HashToken(refreshToken);
        var stored = await Context.RefreshTokens.FirstOrDefaultAsync(t => t.RefreshTokenHash == hash, ct);
        if (stored is null || stored.Audience != RefreshToken.PortalAudience || stored.Revoked || stored.Expiry <= Now)
            throw new UnauthorizedException(InvalidSession);

        var user = await _manager.FindByIdAsync(stored.UserId.ToString());
        if (user is null || user.Status != "Active" || !user.EmailConfirmed || await _manager.IsLockedOutAsync(user))
        {
            stored.Revoked = true;
            await CommitChangesAsync(ct);
            throw new UnauthorizedException(InvalidSession);
        }

        return await OpenSessionAsync(user, stored, ct);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = HashToken(refreshToken);
        var stored = await Context.RefreshTokens
            .FirstOrDefaultAsync(t => t.RefreshTokenHash == hash && t.Audience == RefreshToken.PortalAudience && !t.Revoked, ct);
        if (stored is null) return;
        stored.Revoked = true;
        await CommitChangesAsync(ct);
    }

    // ── Password reset ───────────────────────────────────────────────────────

    /// <summary>Emails a reset link pointing at the portal. Always looks successful: it never says whether the email exists.</summary>
    public async Task ForgotPasswordAsync(string? email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        var user = await _manager.FindByEmailAsync(email.Trim());
        if (user is null || user.Email is null || !await _manager.HasPasswordAsync(user)) return;
        if (string.IsNullOrWhiteSpace(_settings.PortalUrl))
        {
            _logger.LogWarning("Portal password reset requested but PortalAuth:PortalUrl is not set");
            return;
        }

        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await _manager.GeneratePasswordResetTokenAsync(user)));
        var link = $"{_settings.PortalUrl.TrimEnd('/')}/reset-password?token={token}&email={Uri.EscapeDataString(user.Email)}";
        await _publisher.Publish(new ResetPasswordEmail
        {
            Email = user.Email,
            Name = user.FullName ?? "User",
            AppName = _settings.AppName,
            Token = token,
            ResetLink = link,
            Expiry = Now.AddHours(24),
        }, ct);
        await CommitChangesAsync(ct);
    }

    /// <summary>Sets a new password from a reset link and ends every session of the account.</summary>
    public async Task ResetPasswordAsync(string? email, string? token, string? newPassword, CancellationToken ct)
    {
        const string invalid = "This reset link is invalid or has expired.";
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(newPassword))
            throw new GuardException(invalid);

        var user = await _manager.FindByEmailAsync(email.Trim());
        if (user is null) throw new GuardException(invalid);

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            throw new GuardException(invalid);
        }

        var result = await _manager.ResetPasswordAsync(user, decoded, newPassword);
        if (!result.Succeeded)
        {
            // A weak password is the person's to fix; a bad token is not worth explaining.
            var weak = result.Errors.Where(e => e.Code.StartsWith("Password", StringComparison.Ordinal)).Select(e => e.Description).ToList();
            throw new GuardException(weak.Count > 0 ? string.Join(" ", weak) : invalid);
        }

        foreach (var session in await Context.RefreshTokens.Where(t => t.UserId == user.Id && !t.Revoked).ToListAsync(ct))
            session.Revoked = true;
        await _manager.ResetAccessFailedCountAsync(user);
        await CommitChangesAsync(ct);
    }

    // ── Accounts (called by the portal's server) ─────────────────────────────

    /// <summary>
    /// Makes sure a login exists for an invited person. A new one is created with the password they chose and
    /// marked portal-only; if the email already has an account it is left exactly as it is.
    /// </summary>
    public async Task<PortalEnsureAccountResult> EnsureAccountAsync(PortalEnsureAccountRequest request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim();
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new GuardException("A valid email is required.");

        var existing = await _manager.FindByEmailAsync(email);
        if (existing is not null) return new PortalEnsureAccountResult(existing.Id, false);

        if (string.IsNullOrWhiteSpace(request.FullName)) throw new GuardException("A name is required.");
        if (string.IsNullOrEmpty(request.Password)) throw new GuardException("A password is required.");

        var user = NewPortalUser(email, request.FullName);
        var created = await _manager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            throw new GuardException(string.Join(" ", created.Errors.Select(e => e.Description)));

        await CommitChangesAsync(ct);
        return new PortalEnsureAccountResult(user.Id, true);
    }

    /// <summary>A login for someone the portal verified an invitation for: confirmed (the invite email proved it) and hidden from HRIS sign-in.</summary>
    public static User NewPortalUser(string email, string fullName) => new()
    {
        UserName = email,
        Email = email,
        FullName = fullName.Trim(),
        Status = "Active",
        EmailConfirmed = true,
        PortalOnly = true,
    };

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task<PortalSession> OpenSessionAsync(User user, RefreshToken? replacing, CancellationToken ct)
    {
        var now = Now;
        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entry = new RefreshToken
        {
            UserId = user.Id,
            RefreshTokenHash = HashToken(refresh),
            Expiry = now.AddDays(_settings.RefreshDays),
            CreatedAt = now,
            Audience = RefreshToken.PortalAudience,
        };
        await Context.RefreshTokens.AddAsync(entry, ct);
        if (replacing is not null)
        {
            replacing.Revoked = true;
            replacing.LastUsedAt = now;
        }
        await CommitChangesAsync(ct);

        var (access, expires) = CreateAccessToken(user, now);
        return new PortalSession(access, expires, refresh, entry.Expiry, user.Id, user.Email ?? string.Empty, user.FullName);
    }

    private (string Token, DateTime ExpiresAtUtc) CreateAccessToken(User user, DateTime now)
    {
        var expires = now.AddMinutes(_settings.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new("name", user.FullName ?? user.Email ?? string.Empty),
            new(UserTypeClaim, UserTypeValue),
        };
        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(_keys.SigningKey, SecurityAlgorithms.RsaSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
