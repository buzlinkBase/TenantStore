using FluentAssertions;
using Onepunch.AuditEngine.EntityFrameworkCore;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Origins;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// What the Acquisition report reads (origin + early steps per account), and how accounts that pre-date origin tracking
/// are placed from what the system already recorded.
/// </summary>
public class AccountOriginReaderAndBackfillTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static User NewUser(string email, bool confirmed = true) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
        FullName = email,
        Status = "Active",
        EmailConfirmed = confirmed,
    };

    private static RefreshToken Session(Guid userId, DateTime at, string? audience = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        RefreshTokenHash = Guid.NewGuid().ToString(),
        Expiry = at.AddDays(30),
        CreatedAt = at,
        Audience = audience,
    };

    // ── Reader ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reader_ReturnsOnlyAccountsCreatedInTheRange_WithTheirStepsAndNoPersonalDetails()
    {
        var context = AuthTestContextFactory.CreateContext();
        var inside = NewUser("in@x.com").BeganAs(AccountOrigins.ClientInvite, T0.AddDays(2));
        inside.StampFirstHrisSignIn(T0.AddDays(3));
        var before = NewUser("before@x.com").BeganAs(AccountOrigins.EmailSignup, T0.AddDays(-1));
        var after = NewUser("after@x.com").BeganAs(AccountOrigins.GoogleSignup, T0.AddDays(10));
        var undated = NewUser("old@x.com");
        context.Users.AddRange(inside, before, after, undated);
        context.TenantCreationRequests.Add(new TenantCreationRequestStatus { Id = Guid.NewGuid(), UserId = inside.Id, TenantId = Guid.NewGuid(), CreatedAt = T0.AddDays(4) });
        await context.SaveChangesAsync();

        var page = await new AccountOriginReader(context).ReadAsync([], T0, T0.AddDays(7), CancellationToken.None);

        var row = page.Accounts.Should().ContainSingle().Subject;
        row.UserId.Should().Be(inside.Id);
        row.Origin.Should().Be(AccountOrigins.ClientInvite);
        row.CreatedAtUtc.Should().Be(T0.AddDays(2));
        row.FirstHrisSignInAtUtc.Should().Be(T0.AddDays(3));
        row.OwnCompanyRequestedAtUtc.Should().Be(T0.AddDays(4));
        page.UndatedCount.Should().Be(1, "the account with no recorded sign-up date can't be placed on a timeline");
        page.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Reader_ByIds_ReturnsThoseAccounts_EvenWithoutADate()
    {
        var context = AuthTestContextFactory.CreateContext();
        var old = NewUser("old@x.com");
        var other = NewUser("other@x.com").BeganAs(AccountOrigins.StaffInvite, T0);
        context.Users.AddRange(old, other);
        await context.SaveChangesAsync();

        var page = await new AccountOriginReader(context).ReadAsync([old.Id], null, null, CancellationToken.None);

        var row = page.Accounts.Should().ContainSingle().Subject;
        row.UserId.Should().Be(old.Id);
        row.Origin.Should().Be(AccountOrigins.Unknown);
        row.CreatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Reader_WithNeitherIdsNorARange_ReturnsNothing()
    {
        var context = AuthTestContextFactory.CreateContext();
        context.Users.Add(NewUser("a@x.com").BeganAs(AccountOrigins.EmailSignup, T0));
        await context.SaveChangesAsync();

        var page = await new AccountOriginReader(context).ReadAsync([], null, null, CancellationToken.None);

        page.Accounts.Should().BeEmpty();
    }

    // ── Backfill ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Backfill_PlacesEachKindOfOldAccount_FromWhatWasRecorded()
    {
        var context = AuthTestContextFactory.CreateContext();
        var client = NewUser("client@x.com"); client.PortalOnly = true;
        var google = NewUser("google@x.com");
        var email = NewUser("email@x.com");
        var staff = NewUser("staff@x.com");
        var mystery = NewUser("mystery@x.com");
        context.Users.AddRange(client, google, email, staff, mystery);

        context.Invitations.Add(new Invitation { Id = Guid.NewGuid(), TenantName = "Co", Token = Guid.NewGuid().ToString(), Email = "client@x.com", Category = InvitationCategory.ClientUser, Status = InvitationStatus.Accepted, CreatedAt = T0.AddDays(-1), UpdatedAt = T0 });
        context.Set<AuditEventRecord>().Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EventType = "Login",
            EntityId = google.Id.ToString(),
            Description = AccountOriginBackfillService.GoogleSignUpDescription,
            OccurredAtUtc = T0.AddDays(1),
        });
        context.EmailTokens.Add(new EmailToken { Id = Guid.NewGuid(), Email = "email@x.com", TokenType = "", TokenValue = "t", IsUsed = true, Expiry = T0.AddDays(9), CreatedAt = T0.AddDays(2), UpdatedAt = T0.AddDays(2).AddMinutes(3) });
        context.Invitations.Add(new Invitation { Id = Guid.NewGuid(), TenantName = "Co", Token = Guid.NewGuid().ToString(), Email = "staff@x.com", Category = InvitationCategory.Staff, Status = InvitationStatus.Accepted, CreatedAt = T0.AddDays(2), UpdatedAt = T0.AddDays(3) });
        context.RefreshTokens.Add(Session(staff.Id, T0.AddDays(3).AddMinutes(1)));
        await context.SaveChangesAsync();

        var report = await new AccountOriginBackfillService(context).RunAsync(CancellationToken.None);

        report.Classified[AccountOrigins.ClientInvite].Should().Be(1);
        report.Classified[AccountOrigins.GoogleSignup].Should().Be(1);
        report.Classified[AccountOrigins.EmailSignup].Should().Be(1);
        report.Classified[AccountOrigins.StaffInvite].Should().Be(1);
        report.StillUnknown.Should().Be(1);

        client.Origin.Should().Be(AccountOrigins.ClientInvite);
        client.CreatedAtUtc.Should().Be(T0);
        google.Origin.Should().Be(AccountOrigins.GoogleSignup);
        google.CreatedAtUtc.Should().Be(T0.AddDays(1));
        email.Origin.Should().Be(AccountOrigins.EmailSignup);
        email.CreatedAtUtc.Should().Be(T0.AddDays(2));
        email.EmailConfirmedAtUtc.Should().Be(T0.AddDays(2).AddMinutes(3));
        staff.Origin.Should().Be(AccountOrigins.StaffInvite);
        staff.CreatedAtUtc.Should().Be(T0.AddDays(3));
        mystery.Origin.Should().Be(AccountOrigins.Unknown, "nothing recorded says how it began, so it isn't guessed");
        mystery.CreatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Backfill_DoesNotCallAnInviteeAStaffInvite_WhenTheyWereAlreadySigningInBeforeAccepting()
    {
        var context = AuthTestContextFactory.CreateContext();
        var existing = NewUser("existing@x.com");
        context.Users.Add(existing);
        context.Invitations.Add(new Invitation { Id = Guid.NewGuid(), TenantName = "Co", Token = Guid.NewGuid().ToString(), Email = "existing@x.com", Category = InvitationCategory.Staff, Status = InvitationStatus.Accepted, CreatedAt = T0, UpdatedAt = T0.AddDays(5) });
        context.RefreshTokens.Add(Session(existing.Id, T0.AddDays(-30)));
        await context.SaveChangesAsync();

        await new AccountOriginBackfillService(context).RunAsync(CancellationToken.None);

        existing.Origin.Should().Be(AccountOrigins.Unknown, "they already had an account; the invitation only added a company");
    }

    [Fact]
    public async Task Backfill_FillsFirstSignInsFromTheEarliestSessionPerAudience_AndNeverMovesOne()
    {
        var context = AuthTestContextFactory.CreateContext();
        var user = NewUser("a@x.com").BeganAs(AccountOrigins.EmailSignup, T0);
        var recorded = NewUser("b@x.com").BeganAs(AccountOrigins.EmailSignup, T0);
        recorded.StampFirstHrisSignIn(T0.AddDays(1));
        context.Users.AddRange(user, recorded);
        context.RefreshTokens.AddRange(
            Session(user.Id, T0.AddDays(2)),
            Session(user.Id, T0.AddDays(1)),
            Session(user.Id, T0.AddDays(4), RefreshToken.PortalAudience),
            Session(recorded.Id, T0.AddDays(-9)));
        await context.SaveChangesAsync();

        var report = await new AccountOriginBackfillService(context).RunAsync(CancellationToken.None);

        user.FirstHrisSignInAtUtc.Should().Be(T0.AddDays(1));
        user.FirstPortalSignInAtUtc.Should().Be(T0.AddDays(4));
        recorded.FirstHrisSignInAtUtc.Should().Be(T0.AddDays(1), "a recorded first sign-in is never overwritten");
        report.SignInsFilled.Should().Be(2);
    }

    [Fact]
    public async Task Backfill_IsIdempotent()
    {
        var context = AuthTestContextFactory.CreateContext();
        var email = NewUser("email@x.com");
        context.Users.Add(email);
        context.EmailTokens.Add(new EmailToken { Id = Guid.NewGuid(), Email = "email@x.com", TokenType = "", TokenValue = "t", IsUsed = true, Expiry = T0.AddDays(9), CreatedAt = T0, UpdatedAt = T0.AddMinutes(1) });
        await context.SaveChangesAsync();
        var sut = new AccountOriginBackfillService(context);

        await sut.RunAsync(CancellationToken.None);
        var second = await sut.RunAsync(CancellationToken.None);

        email.Origin.Should().Be(AccountOrigins.EmailSignup);
        email.CreatedAtUtc.Should().Be(T0);
        second.Classified.Values.Sum().Should().Be(0, "nothing is left to place");
        second.SignInsFilled.Should().Be(0);
    }

    [Fact]
    public async Task Backfill_IgnoresPasswordResetTokens_WhenLookingForASignupVerification()
    {
        var context = AuthTestContextFactory.CreateContext();
        var user = NewUser("reset@x.com");
        context.Users.Add(user);
        context.EmailTokens.Add(new EmailToken { Id = Guid.NewGuid(), Email = "reset@x.com", TokenType = "user.reset.password", TokenValue = "t", Expiry = T0.AddDays(1), CreatedAt = T0 });
        await context.SaveChangesAsync();

        await new AccountOriginBackfillService(context).RunAsync(CancellationToken.None);

        user.Origin.Should().Be(AccountOrigins.Unknown);
    }
}
