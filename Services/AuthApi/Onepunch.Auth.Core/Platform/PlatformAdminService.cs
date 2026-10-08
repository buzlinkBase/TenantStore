using Microsoft.AspNetCore.Identity;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Exceptions;
using Onepunch.Common.Lib.Platform;

namespace Onepunch.Auth.Core.Platform;

/// <summary>
/// Managing the console's operators (SuperAdmin only): invite, change role, deactivate/reactivate, reset
/// password or authenticator, end sessions. Passwords are never chosen by the admin: a one-time
/// password is generated, returned once, and must be replaced at first sign-in.
///
/// Guard rails: nobody can lock themselves out (no self role change, deactivation or credential
/// reset -- the change-password screen exists for that), and the platform always keeps at least one
/// active SuperAdmin. Every change that reduces access ends the operator's sessions; the 5-minute
/// access token they may still hold is the longest it can linger.
/// Every action is recorded in <c>PlatformAdminAudits</c> (in the same save as the change) and logged.
/// The checks that protect the last SuperAdmin run in a serializable transaction, so two admins
/// demoting each other at the same moment can't both pass.
/// </summary>
public class PlatformAdminService
{
    private readonly PlatformAuthContext _db;
    private readonly PlatformAuthService _auth;
    private readonly IPlatformPasswordGenerator _passwords;
    private readonly TimeProvider _time;
    private readonly ILogger<PlatformAdminService> _logger;
    private readonly PasswordHasher<PlatformUser> _hasher = new();

    public PlatformAdminService(
        PlatformAuthContext db,
        PlatformAuthService auth,
        IPlatformPasswordGenerator passwords,
        TimeProvider time,
        ILogger<PlatformAdminService> logger)
    {
        _db = db;
        _auth = auth;
        _passwords = passwords;
        _time = time;
        _logger = logger;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public async Task<List<PlatformAdminView>> ListAsync(CancellationToken ct)
    {
        var users = await _db.PlatformUsers.AsNoTracking().ToListAsync(ct);
        return users
            .OrderByDescending(u => u.IsActive).ThenBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ToView).ToList();
    }

    public async Task<PlatformAdminCredentials> CreateAsync(CreatePlatformAdminRequest request, Guid actorId, CancellationToken ct)
    {
        var actor = await ActorAsync(actorId, ct);
        var password = _passwords.Create(request.Email ?? string.Empty);
        var user = await _auth.CreateUserAsync(request.Email ?? string.Empty, request.DisplayName ?? string.Empty, request.Role, password, actor, ct);
        Record(PlatformAdminActions.Created, actorId, actor, user, $"as {user.Role}");
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Platform admin {Actor} created operator {UserId} ({Email}) as {Role}", actor, user.Id, user.Email, user.Role);
        return new PlatformAdminCredentials(ToView(user), password);
    }

    public Task<PlatformAdminView> ChangeRoleAsync(Guid id, string role, Guid actorId, CancellationToken ct) =>
        InTransactionAsync(async () =>
        {
            if (!PlatformRoles.IsKnown(role)) throw new GuardException($"Unknown platform role '{role}'.");
            var user = await FindAsync(id, ct);
            RefuseSelf(user, actorId, "change your own role");
            if (user.Role == role) return ToView(user);

            if (user.Role == PlatformRoles.SuperAdmin) await RequireAnotherSuperAdminAsync(user, "demote", ct);

            var actor = await ActorAsync(actorId, ct);
            var before = user.Role;
            user.Role = role;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await _auth.RevokeAllAsync(user.Id, "role-changed", ct, save: false);
            Record(PlatformAdminActions.RoleChanged, actorId, actor, user, $"{before} -> {role}");
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Platform admin {Actor} changed {UserId} from {From} to {To}", actor, id, before, role);
            return ToView(user);
        }, ct);

    public Task<PlatformAdminView> SetActiveAsync(Guid id, bool active, Guid actorId, CancellationToken ct) =>
        InTransactionAsync(async () =>
        {
            var user = await FindAsync(id, ct);
            if (user.IsActive == active) return ToView(user);

            var actor = await ActorAsync(actorId, ct);
            if (!active)
            {
                RefuseSelf(user, actorId, "deactivate yourself");
                if (user.Role == PlatformRoles.SuperAdmin) await RequireAnotherSuperAdminAsync(user, "deactivate", ct);
                user.IsActive = false;
                user.SecurityStamp = Guid.NewGuid().ToString("N");
                await _auth.RevokeAllAsync(user.Id, "deactivated", ct, save: false);
            }
            else
            {
                // Coming back after being switched off: the old password may have been seen by whoever it was
                // switched off for, so it must be replaced at the next sign-in, and nothing from before survives.
                user.IsActive = true;
                user.MustChangePassword = true;
                user.FailedAccessCount = 0;
                user.LockoutEndUtc = null;
                user.SecurityStamp = Guid.NewGuid().ToString("N");
                await _auth.RevokeAllAsync(user.Id, "reactivated", ct, save: false);
            }

            Record(active ? PlatformAdminActions.Reactivated : PlatformAdminActions.Deactivated, actorId, actor, user, null);
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Platform admin {Actor} {Action} operator {UserId}", actor, active ? "reactivated" : "deactivated", id);
            return ToView(user);
        }, ct);

    /// <summary>A new one-time password; the operator must change it at their next sign-in. Also clears a lockout.</summary>
    public async Task<PlatformAdminCredentials> ResetPasswordAsync(Guid id, Guid actorId, CancellationToken ct)
    {
        var user = await FindAsync(id, ct);
        RefuseSelf(user, actorId, "reset your own password here (use Change password)");

        var password = _passwords.Create(user.Email);
        user.PasswordHash = _hasher.HashPassword(user, password);
        user.MustChangePassword = true;
        user.FailedAccessCount = 0;
        user.LockoutEndUtc = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _auth.RevokeAllAsync(user.Id, "password-reset", ct, save: false);
        var actor = await ActorAsync(actorId, ct);
        Record(PlatformAdminActions.PasswordReset, actorId, actor, user, null);
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Platform admin {Actor} reset the password of {UserId}", actor, id);
        return new PlatformAdminCredentials(ToView(user), password);
    }

    /// <summary>For a lost phone: forgets the authenticator so the operator enrols a new one at their next sign-in.</summary>
    public async Task<PlatformAdminView> ResetMfaAsync(Guid id, Guid actorId, CancellationToken ct)
    {
        var user = await FindAsync(id, ct);
        RefuseSelf(user, actorId, "reset your own authenticator");

        user.MfaSecretProtected = null;
        user.MfaEnrolledAtUtc = null;
        user.LastUsedTotpStep = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _auth.RevokeAllAsync(user.Id, "mfa-reset", ct, save: false);
        var actor = await ActorAsync(actorId, ct);
        Record(PlatformAdminActions.MfaReset, actorId, actor, user, null);
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Platform admin {Actor} reset the authenticator of {UserId}", actor, id);
        return ToView(user);
    }

    public async Task RevokeSessionsAsync(Guid id, Guid actorId, CancellationToken ct)
    {
        var user = await FindAsync(id, ct);
        var actor = await ActorAsync(actorId, ct);
        await _auth.RevokeAllAsync(user.Id, "revoked-by-admin", ct, save: false);
        Record(PlatformAdminActions.SessionsRevoked, actorId, actor, user, null);
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Platform admin {Actor} ended the sessions of {UserId}", actor, id);
    }

    // ── Audit ────────────────────────────────────────────────────────────────

    public async Task<PlatformAdminAuditPage> GetAuditAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.PlatformAdminAudits.AsNoTracking();
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PlatformAdminAuditPage(
            rows.Select(x => new PlatformAdminAuditView(x.Id, x.CreatedUtc, x.ActorEmail, x.Action, x.TargetEmail, x.Detail)).ToList(),
            total, page, pageSize);
    }

    // ── Break-glass ──────────────────────────────────────────────────────────

    /// <summary>
    /// Re-seeds a SuperAdmin from configuration when every operator is locked out: creates the account, or
    /// takes the existing one back (active, SuperAdmin, new one-time password, authenticator forgotten,
    /// sessions ended). Acts once per recovery: an operator who has signed in since is left alone, so a
    /// config left switched on can't keep resetting them. Returns false when it skipped.
    /// </summary>
    public async Task<bool> RecoverSuperAdminAsync(string email, string? displayName, string password, CancellationToken ct)
    {
        var errors = PlatformPasswordPolicy.Validate(password, email);
        if (errors.Count > 0) throw new GuardException(string.Join(" ", errors));

        var normalized = PlatformAuthService.NormalizeEmail(email);
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(x => x.NormalizedEmail == normalized, ct);
        if (user is null)
        {
            user = await _auth.CreateUserAsync(email, displayName ?? "Platform Administrator", PlatformRoles.SuperAdmin, password, "recovery", ct);
            Record(PlatformAdminActions.Recovered, null, "bootstrap", user, "created");
            await _db.SaveChangesAsync(ct);
            return true;
        }

        var lastRecovery = await _db.PlatformAdminAudits.AsNoTracking()
            .Where(x => x.TargetId == user.Id && x.Action == PlatformAdminActions.Recovered)
            .OrderByDescending(x => x.CreatedUtc).Select(x => (DateTime?)x.CreatedUtc).FirstOrDefaultAsync(ct);
        if (lastRecovery.HasValue && user.LastLoginAtUtc > lastRecovery) return false;

        user.IsActive = true;
        user.Role = PlatformRoles.SuperAdmin;
        user.PasswordHash = _hasher.HashPassword(user, password);
        user.MustChangePassword = true;
        user.MfaSecretProtected = null;
        user.MfaEnrolledAtUtc = null;
        user.LastUsedTotpStep = null;
        user.FailedAccessCount = 0;
        user.LockoutEndUtc = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _auth.RevokeAllAsync(user.Id, "recovered", ct, save: false);
        Record(PlatformAdminActions.Recovered, null, "bootstrap", user, "taken back");
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void Record(string action, Guid? actorId, string actorEmail, PlatformUser target, string? detail) =>
        _db.PlatformAdminAudits.Add(new PlatformAdminAudit
        {
            CreatedUtc = Now,
            ActorId = actorId,
            ActorEmail = actorEmail,
            Action = action,
            TargetId = target.Id,
            TargetEmail = target.Email,
            Detail = detail,
        });

    /// <summary>Runs the guarded part of a change (check + write) as one serializable unit.</summary>
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var result = await work();
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task<PlatformUser> FindAsync(Guid id, CancellationToken ct) =>
        await _db.PlatformUsers.FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new GuardException("Operator not found.");

    private async Task<string> ActorAsync(Guid actorId, CancellationToken ct) =>
        await _db.PlatformUsers.AsNoTracking().Where(x => x.Id == actorId).Select(x => x.Email).FirstOrDefaultAsync(ct) ?? actorId.ToString();

    private static void RefuseSelf(PlatformUser user, Guid actorId, string what)
    {
        if (user.Id == actorId) throw new GuardException($"You can't {what}. Ask another SuperAdmin.");
    }

    private async Task RequireAnotherSuperAdminAsync(PlatformUser user, string action, CancellationToken ct)
    {
        var others = await _db.PlatformUsers.CountAsync(x => x.Id != user.Id && x.IsActive && x.Role == PlatformRoles.SuperAdmin, ct);
        if (others == 0)
            throw new GuardException($"You can't {action} the last active SuperAdmin. Make someone else a SuperAdmin first.");
    }

    private PlatformAdminView ToView(PlatformUser u) => new(
        u.Id, u.Email, u.DisplayName, u.Role, u.IsActive, u.MfaEnrolledAtUtc.HasValue, u.MustChangePassword,
        _auth.IsLocked(u), u.LastLoginAtUtc, u.CreatedAtUtc, u.CreatedBy);
}
