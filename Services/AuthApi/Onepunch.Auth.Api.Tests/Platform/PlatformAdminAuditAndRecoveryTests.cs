using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib.Exceptions;
using Onepunch.Common.Lib.Platform;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

/// <summary>
/// The operator audit trail (every change to an operator is recorded with who did it), what coming
/// back from deactivation demands, and the break-glass recovery of a SuperAdmin from configuration.
/// </summary>
public class PlatformAdminAuditAndRecoveryTests : IDisposable
{
    private const string Temp = "Temp-Pass-For-Test-1!";
    private const string Recovery = "Recovery-Pass-For-Test-2!";
    private readonly PlatformAuthHarness _h = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    public void Dispose() => _h.Dispose();

    private sealed class FixedPasswords(string password) : IPlatformPasswordGenerator
    {
        public string Create(string email) => password;
    }

    private PlatformAdminService Admins()
    {
        var context = _h.NewContext();
        return new PlatformAdminService(context, _h.NewService(context), new FixedPasswords(Temp), _h.Time, NullLogger<PlatformAdminService>.Instance);
    }

    private async Task<Guid> SeedAsync(string email, string role) => (await _h.SeedUserAsync(email, role)).Id;

    private async Task<List<PlatformAdminAudit>> AuditAsync()
    {
        await using var db = _h.NewContext();
        return await db.PlatformAdminAudits.AsNoTracking().OrderBy(x => x.CreatedUtc).ToListAsync();
    }

    private async Task<PlatformUser> UserAsync(Guid id)
    {
        await using var db = _h.NewContext();
        return await db.PlatformUsers.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private async Task<int> ActiveSessionsAsync(Guid id)
    {
        await using var db = _h.NewContext();
        return await db.PlatformRefreshTokens.CountAsync(x => x.UserId == id && x.RevokedAtUtc == null);
    }

    // ── Audit trail ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryChange_IsRecorded_WithWhoDidItAndToWhom()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        await Admins().CreateAsync(new CreatePlatformAdminRequest("new@example.com", "New", PlatformRoles.Support), boss, _ct);
        var newId = (await Admins().ListAsync(_ct)).Single(a => a.Email == "new@example.com").Id;

        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await Admins().ChangeRoleAsync(newId, PlatformRoles.Billing, boss, _ct);
        await Admins().SetActiveAsync(newId, false, boss, _ct);
        await Admins().SetActiveAsync(newId, true, boss, _ct);
        await Admins().ResetPasswordAsync(newId, boss, _ct);
        await Admins().ResetMfaAsync(newId, boss, _ct);
        await Admins().RevokeSessionsAsync(newId, boss, _ct);

        var rows = await AuditAsync();
        rows.Select(r => r.Action).Should().BeEquivalentTo(
        [
            PlatformAdminActions.Created, PlatformAdminActions.RoleChanged, PlatformAdminActions.Deactivated,
            PlatformAdminActions.Reactivated, PlatformAdminActions.PasswordReset, PlatformAdminActions.MfaReset,
            PlatformAdminActions.SessionsRevoked,
        ]);
        rows.Should().OnlyContain(r => r.ActorId == boss && r.ActorEmail == "boss@example.com" && r.TargetEmail == "new@example.com");
        rows.Single(r => r.Action == PlatformAdminActions.RoleChanged).Detail.Should().Be("Support -> Billing");
    }

    [Fact]
    public async Task ARefusedChange_LeavesNoRecord()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var second = await SeedAsync("second@example.com", PlatformRoles.SuperAdmin);
        await Admins().ChangeRoleAsync(second, PlatformRoles.Support, boss, _ct);
        var before = (await AuditAsync()).Count;

        await Assert.ThrowsAsync<GuardException>(() => Admins().ChangeRoleAsync(boss, PlatformRoles.Support, second, _ct));
        await Assert.ThrowsAsync<GuardException>(() => Admins().ResetPasswordAsync(boss, boss, _ct));

        (await AuditAsync()).Should().HaveCount(before);
    }

    [Fact]
    public async Task TheAuditPage_ListsNewestFirst_AndPages()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var ops = await SeedAsync("ops@example.com", PlatformRoles.Support);
        for (var i = 0; i < 3; i++)
        {
            _h.Time.Advance(TimeSpan.FromMinutes(1));
            await Admins().RevokeSessionsAsync(ops, boss, _ct);
        }
        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await Admins().ResetMfaAsync(ops, boss, _ct);

        var first = await Admins().GetAuditAsync(1, 2, _ct);
        var second = await Admins().GetAuditAsync(2, 2, _ct);

        first.Total.Should().Be(4);
        first.Items.Select(i => i.Action).Should().Equal(PlatformAdminActions.MfaReset, PlatformAdminActions.SessionsRevoked);
        second.Items.Should().HaveCount(2).And.OnlyContain(i => i.Action == PlatformAdminActions.SessionsRevoked);
        (await Admins().GetAuditAsync(0, 1000, _ct)).PageSize.Should().Be(100);
    }

    // ── Coming back ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Reactivating_ForcesAPasswordChange_EndsOldSessions_AndInvalidatesPendingSignIns()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var ops = await SeedAsync("ops@example.com", PlatformRoles.Support);
        await _h.SignInAsync(_h.NewService(), "ops@example.com");
        (await UserAsync(ops)).MustChangePassword.Should().BeFalse();
        await Admins().SetActiveAsync(ops, false, boss, _ct);
        var stampWhileOff = (await UserAsync(ops)).SecurityStamp;

        await Admins().SetActiveAsync(ops, true, boss, _ct);

        var back = await UserAsync(ops);
        back.IsActive.Should().BeTrue();
        back.MustChangePassword.Should().BeTrue();
        back.SecurityStamp.Should().NotBe(stampWhileOff);
        (await ActiveSessionsAsync(ops)).Should().Be(0);
    }

    // ── Break-glass ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Recovery_CreatesTheSuperAdmin_WhenNoSuchOperatorExists()
    {
        await SeedAsync("someone@example.com", PlatformRoles.Support);

        var acted = await Admins().RecoverSuperAdminAsync("root@example.com", "Root", Recovery, _ct);

        acted.Should().BeTrue();
        var root = (await _h.NewContext().PlatformUsers.SingleAsync(x => x.Email == "root@example.com"));
        root.Role.Should().Be(PlatformRoles.SuperAdmin);
        root.MustChangePassword.Should().BeTrue();
        var row = (await AuditAsync()).Single();
        row.Action.Should().Be(PlatformAdminActions.Recovered);
        row.ActorEmail.Should().Be("bootstrap");
        row.ActorId.Should().BeNull();
    }

    [Fact]
    public async Task Recovery_TakesAnExistingOperatorBack_ActiveSuperAdmin_NewPassword_NoAuthenticator_NoSessions()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        await _h.SignInAsync(_h.NewService(), "boss@example.com");
        await using (var db = _h.NewContext())
        {
            var stored = await db.PlatformUsers.SingleAsync(x => x.Id == boss);
            stored.IsActive = false;
            stored.Role = PlatformRoles.Support;
            stored.LockoutEndUtc = _h.Time.GetUtcNow().UtcDateTime.AddHours(1);
            await db.SaveChangesAsync(_ct);
        }

        (await Admins().RecoverSuperAdminAsync("BOSS@example.com", null, Recovery, _ct)).Should().BeTrue();

        var back = await UserAsync(boss);
        back.IsActive.Should().BeTrue();
        back.Role.Should().Be(PlatformRoles.SuperAdmin);
        back.MfaEnrolledAtUtc.Should().BeNull("they enrol a new authenticator at the next sign-in");
        back.LockoutEndUtc.Should().BeNull();
        back.MustChangePassword.Should().BeTrue();
        (await ActiveSessionsAsync(boss)).Should().Be(0);
        var challenge = await _h.NewService().LoginAsync("boss@example.com", Recovery, _ct);
        challenge.Step.Should().Be(PlatformLoginSteps.MfaSetup);
    }

    [Fact]
    public async Task Recovery_LeftSwitchedOn_DoesNotResetSomeoneWhoHasSignedInSince()
    {
        await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        await Admins().RecoverSuperAdminAsync("boss@example.com", null, PlatformAuthHarness.StrongPassword, _ct);
        _h.Time.Advance(TimeSpan.FromMinutes(5));
        await _h.SignInAsync(_h.NewService(), "boss@example.com");
        _h.Time.Advance(TimeSpan.FromMinutes(5));

        var acted = await Admins().RecoverSuperAdminAsync("boss@example.com", null, Recovery, _ct);

        acted.Should().BeFalse();
        await using var db = _h.NewContext();
        (await db.PlatformUsers.SingleAsync(x => x.Email == "boss@example.com")).MfaEnrolledAtUtc.Should().NotBeNull();
        (await db.PlatformAdminAudits.CountAsync(x => x.Action == PlatformAdminActions.Recovered)).Should().Be(1);
    }

    [Fact]
    public async Task Recovery_BeforeTheFirstSignIn_MayRunAgain_SoARestartIsHarmless()
    {
        await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        await Admins().RecoverSuperAdminAsync("boss@example.com", null, Recovery, _ct);
        _h.Time.Advance(TimeSpan.FromMinutes(5));

        (await Admins().RecoverSuperAdminAsync("boss@example.com", null, Recovery, _ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Recovery_RejectsAWeakPassword_AndChangesNothing()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);

        await Assert.ThrowsAsync<GuardException>(() => Admins().RecoverSuperAdminAsync("boss@example.com", null, "short", _ct));

        (await AuditAsync()).Should().BeEmpty();
        (await UserAsync(boss)).MustChangePassword.Should().BeFalse();
    }
}
