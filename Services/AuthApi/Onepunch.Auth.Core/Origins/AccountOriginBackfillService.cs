using Microsoft.EntityFrameworkCore;
using Onepunch.AuditEngine.EntityFrameworkCore;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Origins;

public sealed record AccountOriginBackfillReport(int Scanned, IReadOnlyDictionary<string, int> Classified, int StillUnknown, int SignInsFilled);

/// <summary>
/// Works out, from what the system already recorded, how accounts that pre-date origin tracking began. Safe to run any
/// number of times: it only fills origins still Unknown and dates still empty, and never guesses (an account it can't
/// place stays Unknown with no date).
///   1. a client-portal-only login -> ClientInvite (dated by its accepted client invitation)
///   2. a "Google sign-up" audit event -> GoogleSignup
///   3. an email-verification token for its address -> EmailSignup
///   4. an accepted staff invitation with no sign-in before it -> StaffInvite
/// First sign-ins come from each account's earliest refresh token per audience.
/// </summary>
public sealed class AccountOriginBackfillService(AuthContext context)
{
    private const int Batch = 500;
    public const string GoogleSignUpDescription = "Signed in (Google sign-up)";
    private static readonly TimeSpan SignInBeforeAcceptance = TimeSpan.FromMinutes(5);

    public async Task<AccountOriginBackfillReport> RunAsync(CancellationToken ct)
    {
        var classified = AccountOrigins.All.ToDictionary(o => o, _ => 0);
        int scanned = 0, stillUnknown = 0, signIns = 0;
        var lastId = Guid.Empty;

        while (true)
        {
            var users = await context.Users.IgnoreQueryFilters()
                .Where(u => u.Id.CompareTo(lastId) > 0
                            && (u.Origin == AccountOrigins.Unknown || u.FirstHrisSignInAtUtc == null || u.FirstPortalSignInAtUtc == null))
                .OrderBy(u => u.Id).Take(Batch).ToListAsync(ct);
            if (users.Count == 0) break;
            lastId = users[^1].Id;
            scanned += users.Count;

            var ids = users.Select(u => u.Id).ToList();
            var emails = users.Where(u => u.Email != null).Select(u => u.Email!.ToLower()).Distinct().ToList();

            var firstSessions = (await context.RefreshTokens.IgnoreQueryFilters().AsNoTracking()
                    .Where(t => ids.Contains(t.UserId))
                    .GroupBy(t => new { t.UserId, t.Audience })
                    .Select(g => new { g.Key.UserId, g.Key.Audience, At = g.Min(t => t.CreatedAt) })
                    .ToListAsync(ct))
                .ToLookup(x => x.UserId);

            var googleSignUps = (await context.Set<AuditEventRecord>().AsNoTracking()
                    .Where(e => e.Description == GoogleSignUpDescription && e.EntityId != null)
                    .Select(e => new { e.EntityId, e.OccurredAtUtc })
                    .ToListAsync(ct))
                .Where(e => Guid.TryParse(e.EntityId, out var g) && ids.Contains(g))
                .GroupBy(e => Guid.Parse(e.EntityId!))
                .ToDictionary(g => g.Key, g => g.Min(e => e.OccurredAtUtc));

            // Email verification tokens carry no type and no user id (they're keyed by address); reset tokens have a type.
            var verifications = (await context.EmailTokens.IgnoreQueryFilters().AsNoTracking()
                    .Where(t => (t.TokenType == null || t.TokenType == "") && emails.Contains(t.Email.ToLower()))
                    .Select(t => new { Email = t.Email.ToLower(), t.CreatedAt, t.IsUsed, t.UpdatedAt })
                    .ToListAsync(ct))
                .GroupBy(t => t.Email)
                .ToDictionary(g => g.Key, g => (Created: g.Min(t => t.CreatedAt), Confirmed: g.Where(t => t.IsUsed).Select(t => t.UpdatedAt).Min()));

            var invitations = (await context.Invitations.IgnoreQueryFilters().AsNoTracking()
                    .Where(i => i.Status == InvitationStatus.Accepted && emails.Contains(i.Email.ToLower()))
                    .Select(i => new { Email = i.Email.ToLower(), i.Category, i.UpdatedAt, i.CreatedAt })
                    .ToListAsync(ct))
                .GroupBy(i => (i.Email, i.Category))
                .ToDictionary(g => g.Key, g => g.Min(i => i.UpdatedAt ?? i.CreatedAt));

            foreach (var user in users)
            {
                var sessions = firstSessions[user.Id].ToList();
                var firstHris = sessions.Where(s => s.Audience == null).Select(s => (DateTime?)s.At).Min();
                var firstPortal = sessions.Where(s => s.Audience == RefreshToken.PortalAudience).Select(s => (DateTime?)s.At).Min();
                if (user.FirstHrisSignInAtUtc is null && firstHris is not null) { user.FirstHrisSignInAtUtc = firstHris; signIns++; }
                if (user.FirstPortalSignInAtUtc is null && firstPortal is not null) { user.FirstPortalSignInAtUtc = firstPortal; signIns++; }

                if (user.Origin != AccountOrigins.Unknown) continue;

                var email = user.Email?.ToLower() ?? string.Empty;
                if (user.PortalOnly)
                {
                    DateTime? accepted = invitations.TryGetValue((email, InvitationCategory.ClientUser), out var at) ? at : null;
                    Place(user, AccountOrigins.ClientInvite, accepted, accepted);
                }
                else if (googleSignUps.TryGetValue(user.Id, out var google))
                {
                    Place(user, AccountOrigins.GoogleSignup, google, google);
                }
                else if (verifications.TryGetValue(email, out var verification))
                {
                    Place(user, AccountOrigins.EmailSignup, verification.Created, user.EmailConfirmed ? verification.Confirmed : null);
                }
                else if (invitations.TryGetValue((email, InvitationCategory.Staff), out var staff)
                         && (firstHris is null || firstHris >= staff - SignInBeforeAcceptance))
                {
                    Place(user, AccountOrigins.StaffInvite, staff, staff);
                }

                if (user.Origin == AccountOrigins.Unknown) stillUnknown++;
                else classified[user.Origin]++;
            }

            await context.SaveChangesAsync(ct);
        }

        return new AccountOriginBackfillReport(scanned, classified, stillUnknown, signIns);
    }

    private static void Place(User user, string origin, DateTime? created, DateTime? confirmed)
    {
        user.BeganAs(origin, created ?? default);
        user.CreatedAtUtc = created;
        user.EmailConfirmedAtUtc ??= confirmed;
    }
}
