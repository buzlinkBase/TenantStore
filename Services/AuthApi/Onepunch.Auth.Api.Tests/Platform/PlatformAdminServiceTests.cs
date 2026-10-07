using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Exceptions;
using Onepunch.Common.Lib.Platform;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

public class PlatformAdminServiceTests : IDisposable
{
    private const string Temp = "Temp-Pass-For-Test-1!";
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

    private async Task<PlatformUser> UserAsync(Guid id)
    {
        await using var db = _h.NewContext();
        return await db.PlatformUsers.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private Task<Guid> SeedAsync(string email, string role) => SeedIdAsync(email, role);

    private async Task<Guid> SeedIdAsync(string email, string role) => (await _h.SeedUserAsync(email, role)).Id;

    private async Task<int> ActiveSessionsAsync(Guid id)
    {
        await using var db = _h.NewContext();
        return await db.PlatformRefreshTokens.CountAsync(x => x.UserId == id && x.RevokedAtUtc == null);
    }

    // ── Invite ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_MakesAnOperatorWithATemporaryPassword_ThatMustBeChanged()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);

        var created = await Admins().CreateAsync(new CreatePlatformAdminRequest("new@example.com", "New Person", PlatformRoles.Support), boss, _ct);

        created.TemporaryPassword.Should().Be(Temp);
        created.Admin.Role.Should().Be(PlatformRoles.Support);
        created.Admin.MustChangePassword.Should().BeTrue();
        created.Admin.MfaEnrolled.Should().BeFalse();
        created.Admin.CreatedBy.Should().Be("boss@example.com");
        var challenge = await _h.NewService().LoginAsync("new@example.com", Temp, _ct);
        challenge.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task Create_RefusesADuplicateEmailAndAnUnknownRole()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);

        await Assert.ThrowsAsync<GuardException>(() => Admins().CreateAsync(new CreatePlatformAdminRequest("BOSS@example.com", "Dup", PlatformRoles.Support), boss, _ct));
        await Assert.ThrowsAsync<GuardException>(() => Admins().CreateAsync(new CreatePlatformAdminRequest("x@example.com", "X", "Root"), boss, _ct));
    }

    [Fact]
    public async Task List_ShowsActiveFirst_WithMfaAndLockStatus_AndNoSecrets()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var other = await SeedAsync("zed@example.com", PlatformRoles.Billing);
        await Admins().SetActiveAsync(other, false, boss, _ct);

        var list = await Admins().ListAsync(_ct);

        list.Select(a => a.Email).Should().Equal("boss@example.com", "zed@example.com");
        list[1].IsActive.Should().BeFalse();
        typeof(PlatformAdminView).GetProperties().Select(p => p.Name).Should().NotContain(["PasswordHash", "MfaSecretProtected", "SecurityStamp"]);
    }

    // ── Role ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeRole_UpdatesTheRole_AndEndsTheirSessions()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var ops = await SeedAsync("ops@example.com", PlatformRoles.Support);
        await _h.SignInAsync(_h.NewService(), "ops@example.com");
        (await ActiveSessionsAsync(ops)).Should().Be(1);

        var view = await Admins().ChangeRoleAsync(ops, PlatformRoles.Billing, boss, _ct);

        view.Role.Should().Be(PlatformRoles.Billing);
        (await UserAsync(ops)).Role.Should().Be(PlatformRoles.Billing);
        (await ActiveSessionsAsync(ops)).Should().Be(0);
    }

    [Fact]
    public async Task ChangeRole_RefusesSelf_AnUnknownRole_AndDemotingTheLastSuperAdmin()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var second = await SeedAsync("second@example.com", PlatformRoles.SuperAdmin);

        await Assert.ThrowsAsync<GuardException>(() => Admins().ChangeRoleAsync(boss, PlatformRoles.Support, boss, _ct));
        await Assert.ThrowsAsync<GuardException>(() => Admins().ChangeRoleAsync(second, "Root", boss, _ct));

        await Admins().ChangeRoleAsync(second, PlatformRoles.Support, boss, _ct);
        var lastOne = await Assert.ThrowsAsync<GuardException>(() => Admins().ChangeRoleAsync(boss, PlatformRoles.Support, second, _ct));
        lastOne.Message.Should().Contain("last active SuperAdmin");
    }

    [Fact]
    public async Task TheLastActiveSuperAdmin_CanNotBeDemotedOrDeactivated_ByAnotherSuperAdmin()
    {
        // The actor is a SuperAdmin too, so "another active SuperAdmin" must not count a deactivated one.
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var second = await SeedAsync("second@example.com", PlatformRoles.SuperAdmin);
        await Admins().SetActiveAsync(second, false, boss, _ct);
        var third = await SeedAsync("third@example.com", PlatformRoles.SuperAdmin);
        await Admins().SetActiveAsync(boss, false, third, _ct);

        // Only `third` is left active: no other SuperAdmin may act on it, and it may not act on itself.
        await Assert.ThrowsAsync<GuardException>(() => Admins().SetActiveAsync(third, false, third, _ct));
        await Admins().SetActiveAsync(second, true, third, _ct);
        await Admins().SetActiveAsync(third, false, second, _ct);
        (await UserAsync(third)).IsActive.Should().BeFalse();

        var refused = await Assert.ThrowsAsync<GuardException>(() => Admins().SetActiveAsync(second, false, boss, _ct));
        refused.Message.Should().Contain("last active SuperAdmin");
    }

    // ── Deactivate / reactivate ──────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_BlocksSignIn_AndEndsSessions_ThenReactivateRestoresIt_AndClearsALockout()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var ops = await SeedAsync("ops@example.com", PlatformRoles.Billing);
        var session = await _h.SignInAsync(_h.NewService(), "ops@example.com");

        await Admins().SetActiveAsync(ops, false, boss, _ct);

        (await ActiveSessionsAsync(ops)).Should().Be(0);
        await Assert.ThrowsAsync<UnauthorizedException>(() => _h.NewService().RefreshAsync(session.RefreshToken, _ct));
        await Assert.ThrowsAsync<UnauthorizedException>(() => _h.NewService().LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct));

        await using (var db = _h.NewContext())
        {
            var stored = await db.PlatformUsers.SingleAsync(x => x.Id == ops);
            stored.LockoutEndUtc = _h.Time.GetUtcNow().UtcDateTime.AddHours(1);
            stored.FailedAccessCount = 3;
            await db.SaveChangesAsync();
        }
        var view = await Admins().SetActiveAsync(ops, true, boss, _ct);

        view.IsActive.Should().BeTrue();
        view.IsLocked.Should().BeFalse();
        (await _h.NewService().LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task Deactivate_RefusesSelf()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        await SeedAsync("second@example.com", PlatformRoles.SuperAdmin);

        await Assert.ThrowsAsync<GuardException>(() => Admins().SetActiveAsync(boss, false, boss, _ct));
    }

    // ── Credentials ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_SetsANewOneTimePassword_ForcesAChange_AndEndsSessions()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var ops = await SeedAsync("ops@example.com", PlatformRoles.Billing);
        await _h.SignInAsync(_h.NewService(), "ops@example.com");

        var result = await Admins().ResetPasswordAsync(ops, boss, _ct);

        result.TemporaryPassword.Should().Be(Temp);
        result.Admin.MustChangePassword.Should().BeTrue();
        (await ActiveSessionsAsync(ops)).Should().Be(0);
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _h.NewService().LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct));
        (await _h.NewService().LoginAsync("ops@example.com", Temp, _ct)).MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_AlsoUnlocksTheAccount_ButNotYourOwn()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var ops = await SeedAsync("ops@example.com", PlatformRoles.Billing);
        await using (var db = _h.NewContext())
        {
            var stored = await db.PlatformUsers.SingleAsync(x => x.Id == ops);
            stored.LockoutEndUtc = _h.Time.GetUtcNow().UtcDateTime.AddHours(1);
            await db.SaveChangesAsync();
        }

        await Admins().ResetPasswordAsync(ops, boss, _ct);

        (await UserAsync(ops)).LockoutEndUtc.Should().BeNull();
        await Assert.ThrowsAsync<GuardException>(() => Admins().ResetPasswordAsync(boss, boss, _ct));
    }

    [Fact]
    public async Task ResetMfa_ForgetsTheAuthenticator_SoTheyEnrolAgain_ButNotYourOwn()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var ops = await SeedAsync("ops@example.com", PlatformRoles.Billing);
        await _h.SignInAsync(_h.NewService(), "ops@example.com");
        (await UserAsync(ops)).MfaEnrolledAtUtc.Should().NotBeNull();

        var view = await Admins().ResetMfaAsync(ops, boss, _ct);

        view.MfaEnrolled.Should().BeFalse();
        var stored = await UserAsync(ops);
        stored.MfaSecretProtected.Should().BeNull();
        stored.LastUsedTotpStep.Should().BeNull();
        (await ActiveSessionsAsync(ops)).Should().Be(0);
        (await _h.NewService().LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct)).Step.Should().Be(PlatformLoginSteps.MfaSetup);
        await Assert.ThrowsAsync<GuardException>(() => Admins().ResetMfaAsync(boss, boss, _ct));
    }

    [Fact]
    public async Task RevokeSessions_EndsEverySession_AndSaysSoForAnUnknownOperator()
    {
        var boss = await SeedAsync("boss@example.com", PlatformRoles.SuperAdmin);
        var ops = await SeedAsync("ops@example.com", PlatformRoles.Billing);
        await _h.SignInAsync(_h.NewService(), "ops@example.com");

        await Admins().RevokeSessionsAsync(ops, boss, _ct);

        (await ActiveSessionsAsync(ops)).Should().Be(0);
        await Assert.ThrowsAsync<GuardException>(() => Admins().RevokeSessionsAsync(Guid.NewGuid(), boss, _ct));
    }

    // ── Generated passwords ──────────────────────────────────────────────────

    [Fact]
    public void GeneratedPasswords_AlwaysSatisfyThePolicy_AndDiffer()
    {
        var generator = new PlatformPasswordGenerator();

        var passwords = Enumerable.Range(0, 200).Select(_ => generator.Create("operator@example.com")).ToList();

        passwords.Should().OnlyContain(p => PlatformPasswordPolicy.Validate(p, "operator@example.com").Count == 0);
        passwords.Distinct().Count().Should().Be(passwords.Count);
    }

    [Fact]
    public void ThePlatformRoleMap_GivesOnlySuperAdminTheAdminsPermission()
    {
        PlatformRolePermissions.For(PlatformRoles.SuperAdmin).Should().Contain(PlatformPermissions.AdminsManage);
        PlatformRolePermissions.For(PlatformRoles.Billing).Should().NotContain(PlatformPermissions.AdminsManage);
        PlatformRolePermissions.For(PlatformRoles.Support).Should().NotContain(PlatformPermissions.AdminsManage);
    }
}
