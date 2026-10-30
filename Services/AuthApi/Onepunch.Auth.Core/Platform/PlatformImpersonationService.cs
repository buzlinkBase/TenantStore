using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using OnePunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Cache;
using Onepunch.Common.Lib.Exceptions;
using Onepunch.Common.Lib.Platform;
using Onepunch.Common.Lib.Security;

namespace Onepunch.Auth.Core.Platform;

/// <summary>
/// A support operator signing in as a company user, from the Platform Admin Console. Three steps:
/// <see cref="StartAsync"/> checks the operator may (permission, reason, <see cref="ImpersonationPolicy"/>)
/// and records the session; <see cref="ExchangeAsync"/> redeems the one-time code for an access token
/// that is never refreshed; <see cref="StopAsync"/> ends it, which every API honours straight away
/// through <see cref="ImpersonationClaims.EndedCacheKey"/>. The start and stop are written to the operator audit.
/// </summary>
public class PlatformImpersonationService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);

    private readonly PlatformAuthContext _db;
    private readonly UserManager<User> _users;
    private readonly MembershipCacheService _memberships;
    private readonly JwtService _jwt;
    private readonly ICacheService _cache;
    private readonly TimeProvider _time;
    private readonly ILogger<PlatformImpersonationService> _logger;

    public PlatformImpersonationService(
        PlatformAuthContext db,
        UserManager<User> users,
        MembershipCacheService memberships,
        JwtService jwt,
        ICacheService cache,
        TimeProvider time,
        ILogger<PlatformImpersonationService> logger)
    {
        _db = db;
        _users = users;
        _memberships = memberships;
        _jwt = jwt;
        _cache = cache;
        _time = time;
        _logger = logger;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    public async Task<StartImpersonationResult> StartAsync(Guid operatorId, StartImpersonationRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason)) throw new GuardException("Enter why you need to sign in as this user.");
        if (reason.Length > 500) throw new GuardException("The reason is too long (500 characters at most).");

        var operatorUser = await _db.PlatformUsers.FirstOrDefaultAsync(x => x.Id == operatorId && x.IsActive, ct)
            ?? throw new GuardException("Your operator account is not active.");
        if (!PlatformRolePermissions.For(operatorUser.Role).Contains(PlatformPermissions.ImpersonateUsers))
            throw new GuardException("Your role can't sign in as a user.");

        var target = await _users.FindByIdAsync(request.TargetUserId.ToString())
            ?? throw new GuardException("That user doesn't exist.");
        var membership = (await _memberships.GetMembershipsAsync(target.Id))
            .FirstOrDefault(m => m.TenantId == request.TenantId)
            ?? throw new GuardException("That user isn't a member of that company.");

        if (!ImpersonationPolicy.CanImpersonate(operatorUser.Role, membership.Roles))
        {
            _logger.LogWarning("Impersonation refused: operator {Operator} ({Role}) -> user {Target} roles {Roles}",
                operatorUser.Email, operatorUser.Role, target.Id, string.Join(",", membership.Roles));
            throw new GuardException("Your role can't sign in as a user with this level of access.");
        }

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = Now;
        var session = new ImpersonationSession
        {
            StartedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_jwt.ImpersonationMaxMinutes),
            OperatorId = operatorUser.Id,
            OperatorEmail = operatorUser.Email,
            OperatorRole = operatorUser.Role,
            TargetUserId = target.Id,
            TargetEmail = target.Email ?? string.Empty,
            TenantId = request.TenantId,
            TenantName = membership.Name ?? string.Empty,
            Reason = reason,
            ExchangeCodeHash = HashCode(code),
            ExchangeCodeExpiresAtUtc = now.Add(CodeLifetime),
        };
        _db.ImpersonationSessions.Add(session);
        if (!IsUnaudited(session.OperatorRole))
            _db.PlatformAdminAudits.Add(Audit(operatorUser.Id, operatorUser.Email, ImpersonationActions.Started, session,
                $"{membership.Name}: {reason}"));
        await _db.SaveChangesAsync(ct);

        return new StartImpersonationResult { SessionId = session.Id, ExchangeCode = code, ExpiresAtUtc = session.ExchangeCodeExpiresAtUtc };
    }

    /// <summary>Redeems the one-time code. Every failure is the same message, so a guessed code learns nothing.</summary>
    public async Task<ImpersonationSessionResponse> ExchangeAsync(string? code, CancellationToken ct)
    {
        const string Invalid = "This sign-in link is no longer valid.";
        if (string.IsNullOrWhiteSpace(code)) throw new GuardException(Invalid);

        var hash = HashCode(code.Trim());
        var now = Now;
        var session = await _db.ImpersonationSessions.FirstOrDefaultAsync(x => x.ExchangeCodeHash == hash, ct);
        if (session == null || session.ExchangedAtUtc != null || session.EndedAtUtc != null
            || session.ExchangeCodeExpiresAtUtc < now || session.ExpiresAtUtc <= now)
            throw new GuardException(Invalid);

        // Claim the code before anything else, so two requests racing on it can't both win.
        session.ExchangedAtUtc = now;
        await _db.SaveChangesAsync(ct);

        var target = await _users.FindByIdAsync(session.TargetUserId.ToString()) ?? throw new GuardException(Invalid);
        var membership = (await _memberships.GetMembershipsAsync(target.Id))
            .FirstOrDefault(m => m.TenantId == session.TenantId) ?? throw new GuardException(Invalid);

        // Re-checked at redemption: the target's roles may have changed since the operator asked.
        if (!ImpersonationPolicy.CanImpersonate(session.OperatorRole, membership.Roles)) throw new GuardException(Invalid);

        var token = await _jwt.CreateImpersonationTokenAsync(
            target, session.TenantId, session.TenantName, membership.Roles, membership.Permissions, session);

        return new ImpersonationSessionResponse
        {
            AccessToken = token,
            // Same reason as in the token: stored values come back without a Kind, and the client needs the Z.
            Expiry = DateTime.SpecifyKind(session.ExpiresAtUtc, DateTimeKind.Utc),
            SessionId = session.Id,
            Name = target.FullName ?? target.Email ?? string.Empty,
            Email = target.Email ?? string.Empty,
            Roles = membership.Roles,
            Permissions = membership.Permissions,
            Tenants = [membership],
            OperatorEmail = session.OperatorEmail,
        };
    }

    /// <summary>Ends the session. Idempotent: stopping an ended or unknown session does nothing.</summary>
    public async Task StopAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _db.ImpersonationSessions.FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session == null || session.EndedAtUtc != null) return;

        session.EndedAtUtc = Now;
        if (!IsUnaudited(session.OperatorRole))
            _db.PlatformAdminAudits.Add(Audit(session.OperatorId, session.OperatorEmail, ImpersonationActions.Stopped, session,
                $"{session.TenantName}: session {session.Id}"));
        await _db.SaveChangesAsync(ct);

        var remaining = session.ExpiresAtUtc - Now;
        if (remaining > TimeSpan.Zero)
            await _cache.SetAsync(ImpersonationClaims.EndedCacheKey(session.Id), true, remaining + TimeSpan.FromMinutes(1));
    }

    /// <summary>A SuperAdmin's sessions leave no operator-audit rows (the session record itself stays, it drives expiry and stop).</summary>
    public static bool IsUnaudited(string? operatorRole) => operatorRole == PlatformRoles.SuperAdmin;

    private PlatformAdminAudit Audit(Guid operatorId, string operatorEmail, string action, ImpersonationSession session, string detail) => new()
    {
        CreatedUtc = Now,
        ActorId = operatorId,
        ActorEmail = operatorEmail,
        Action = action,
        TargetId = session.TargetUserId,
        TargetEmail = session.TargetEmail,
        Detail = detail.Length > 500 ? detail[..500] : detail,
    };
}
