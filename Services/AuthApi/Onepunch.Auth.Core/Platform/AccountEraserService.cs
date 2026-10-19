using Microsoft.EntityFrameworkCore;
using Onepunch.AuditEngine.EntityFrameworkCore;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.DTO;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Platform;

/// <summary>
/// Physically deletes the rows a query selects -- no soft delete. Strategy seam: production deletes
/// in the database (<see cref="RelationalRowEraser"/>); tests over the InMemory provider, which can't
/// run set-based deletes, plug in their own.
/// </summary>
public interface IRowEraser
{
    Task<int> EraseAsync<T>(IQueryable<T> rows, CancellationToken token) where T : class;
}

/// <summary>One DELETE statement per call, inside the unit of work's transaction.</summary>
public sealed class RelationalRowEraser : IRowEraser
{
    public Task<int> EraseAsync<T>(IQueryable<T> rows, CancellationToken token) where T : class =>
        rows.ExecuteDeleteAsync(token);
}

/// <summary>What one erasure (a whole account, or one company of it) asks Auth Service to do.</summary>
public sealed record ErasureOrder(
    string Action,
    Guid RequestId,
    Guid OwnerUserId,
    string OwnerEmail,
    IReadOnlyList<Guid> TenantIds,
    IReadOnlyList<string> CompanyNames,
    IReadOnlyList<Guid> DeleteUserIds,
    IReadOnlyList<Guid> DetachUserIds,
    string RequestedBy,
    string ApprovedBy,
    string Reason)
{
    public static ErasureOrder From(AccountHardDeleted m) => new(
        AccountEraserService.HardDeletedAction, m.RequestId, m.OwnerUserId, m.OwnerEmail, m.TenantIds, m.CompanyNames,
        m.DeleteUserIds, m.DetachUserIds, m.RequestedBy, m.ApprovedBy, m.Reason);

    public static ErasureOrder From(CompanyHardDeleted m) => new(
        AccountEraserService.CompanyHardDeletedAction, m.RequestId, m.OwnerUserId, m.OwnerEmail, [m.TenantId], [m.CompanyName],
        m.DeleteUserIds, m.DetachUserIds, m.RequestedBy, m.ApprovedBy, m.Reason);
}

/// <summary>
/// Auth Service's part of erasing an account (<see cref="AccountHardDeleted"/>) or one of its companies
/// (<see cref="CompanyHardDeleted"/>), sent by Tenant Service once the companies and its own rows are gone:
/// <list type="bullet">
/// <item>deletes the logins that existed only for this account, with their sessions, tokens,
/// invitations and sign-in audit trail (the Identity tables cascade from the user);</item>
/// <item>detaches the people who also belong elsewhere: a default company that was erased is cleared,
/// and their sessions are told to refresh;</item>
/// <item>writes the one operator log line that outlives the account (<see cref="PlatformAdminAudits"/>),
/// keyed by the request id so a redelivery doesn't write it twice.</item>
/// </list>
/// Idempotent throughout.
/// </summary>
public class AccountEraserService
{
    public const string HardDeletedAction = "account.hard-deleted";
    public const string CompanyHardDeletedAction = "company.hard-deleted";
    private const int MaxDetailLength = 500;

    private readonly IUnitOfWorkService _uow;
    private readonly PlatformAuthContext _platform;
    private readonly IRowEraser _rows;
    private readonly MembershipCacheService _membershipCache;
    private readonly TenantNotificationService _notifications;
    private readonly TimeProvider _clock;

    public AccountEraserService(
        IUnitOfWorkService uow,
        PlatformAuthContext platform,
        IRowEraser rows,
        MembershipCacheService membershipCache,
        TenantNotificationService notifications,
        TimeProvider clock)
    {
        _uow = uow;
        _platform = platform;
        _rows = rows;
        _membershipCache = membershipCache;
        _notifications = notifications;
        _clock = clock;
    }

    private AuthContext Context => _uow.Context;

    private IQueryable<T> All<T>() where T : class => Context.Set<T>().IgnoreQueryFilters();

    public Task EraseAsync(AccountHardDeleted message, CancellationToken token) => EraseAsync(ErasureOrder.From(message), token);

    public Task EraseAsync(CompanyHardDeleted message, CancellationToken token) => EraseAsync(ErasureOrder.From(message), token);

    public async Task EraseAsync(ErasureOrder message, CancellationToken token)
    {
        var tenantIds = message.TenantIds.Distinct().ToList();
        var deleteIds = message.DeleteUserIds.Distinct().ToList();
        var detachIds = message.DetachUserIds.Distinct().Except(deleteIds).ToList();
        var deleteIdsNullable = deleteIds.Select(id => (Guid?)id).ToList();
        var tenantIdsNullable = tenantIds.Select(id => (Guid?)id).ToList();
        var organizations = tenantIds.Select(id => id.ToString()).ToList();
        var actors = deleteIds.Select(id => id.ToString()).ToList();

        // Named in the operator log: whoever keeps a login because they belong to other companies.
        var keptEmails = await Context.Users.AsNoTracking()
            .Where(u => detachIds.Contains(u.Id))
            .Select(u => u.Email ?? u.UserName ?? u.Id.ToString())
            .OrderBy(e => e)
            .ToListAsync(token);

        // Detach first, while the rows are still tracked normally.
        var detached = await Context.Users
            .Where(u => detachIds.Contains(u.Id) && u.DefaultTenantId != null && tenantIds.Contains(u.DefaultTenantId.Value))
            .ToListAsync(token);
        foreach (var user in detached)
        {
            user.DefaultTenantId = null;
            user.DefaultTenantName = null;
            user.DefaultTenantRoles = new();
        }

        await _rows.EraseAsync(All<AuditEventRecord>()
            .Where(a => organizations.Contains(a.OrganizationId!) || actors.Contains(a.ActorId!)), token);
        await _rows.EraseAsync(All<RefreshToken>()
            .Where(t => deleteIds.Contains(t.UserId) || tenantIdsNullable.Contains(t.TenantId)), token);
        await _rows.EraseAsync(All<EmailToken>().Where(t => deleteIdsNullable.Contains(t.UserId)), token);
        await _rows.EraseAsync(All<ApiToken>()
            .Where(t => deleteIdsNullable.Contains(t.UserId) || tenantIdsNullable.Contains(t.TenantId)), token);
        await _rows.EraseAsync(All<Invitation>().Where(i => tenantIds.Contains(i.TenantId) || deleteIds.Contains(i.UserId)), token);
        await _rows.EraseAsync(All<InvitationBatch>().Where(b => tenantIds.Contains(b.TenantId) || deleteIds.Contains(b.InvitedByUserId)), token);
        await _rows.EraseAsync(All<TenantCreationRequestStatus>()
            .Where(r => tenantIds.Contains(r.TenantId) || deleteIds.Contains(r.UserId)), token);
        // Roles, claims, external logins and stored tokens cascade from the user in the database.
        await _rows.EraseAsync(Context.Users.Where(u => deleteIds.Contains(u.Id)), token);

        await _uow.CommitChangesAsync($"{message.Action} {message.RequestId}", token);

        foreach (var userId in deleteIds.Concat(detachIds))
        {
            await _membershipCache.InvalidateAsync(userId);
        }
        var tenantId = tenantIds.FirstOrDefault();
        foreach (var userId in detachIds)
        {
            await _notifications.NotifyRolesChanged(userId, new RolesChangedNotification { TenantId = tenantId });
        }
        foreach (var userId in deleteIds)
        {
            await _notifications.NotifySessionRevoked(userId, new SessionRevokedNotification { TenantId = tenantId });
        }

        await RecordAsync(message, keptEmails, token);
    }

    private async Task RecordAsync(ErasureOrder message, IReadOnlyList<string> keptEmails, CancellationToken token)
    {
        if (await _platform.PlatformAdminAudits.AsNoTracking()
                .AnyAsync(a => a.Action == message.Action && a.TargetId == message.RequestId, token))
            return;

        var detail = $"Requested by {Email(message.RequestedBy)}, approved by {Email(message.ApprovedBy)}. "
            + $"Companies: {string.Join(", ", message.CompanyNames)}. "
            + $"Logins deleted: {message.DeleteUserIds.Count}, kept: {message.DetachUserIds.Count}. "
            + $"Reason: {message.Reason}"
            + KeptSentence(keptEmails, MaxDetailLength);
        // The list goes last and is shortened to fit, so the facts above always survive.
        if (detail.Length > MaxDetailLength && keptEmails.Count > 0)
        {
            var head = detail[..detail.IndexOf(" Logins kept", StringComparison.Ordinal)];
            detail = head + KeptSentence(keptEmails, MaxDetailLength - head.Length);
        }
        _platform.PlatformAdminAudits.Add(new PlatformAdminAudit
        {
            CreatedUtc = _clock.GetUtcNow().UtcDateTime,
            ActorEmail = Email(message.ApprovedBy),
            Action = message.Action,
            TargetId = message.RequestId,
            TargetEmail = message.OwnerEmail,
            Detail = detail.Length > MaxDetailLength ? detail[..MaxDetailLength] : detail,
        });
        await _platform.SaveChangesAsync(token);
    }

    /// <summary>" Logins kept (they belong to other companies): a, b, +N more." -- as many emails as fit in <paramref name="room"/>.</summary>
    private static string KeptSentence(IReadOnlyList<string> emails, int room)
    {
        if (emails.Count == 0) return string.Empty;
        const string start = " Logins kept (they belong to other companies): ";
        for (var shown = emails.Count; shown >= 0; shown--)
        {
            var rest = emails.Count - shown;
            var sentence = start + string.Join(", ", emails.Take(shown).Append(rest > 0 ? $"+{rest} more" : null).OfType<string>()) + ".";
            if (sentence.Length <= room || shown == 0) return sentence.Length <= room ? sentence : string.Empty;
        }
        return string.Empty;
    }

    private static string Email(string actor) =>
        actor.StartsWith("platform:", StringComparison.Ordinal) ? actor["platform:".Length..] : actor;
}
