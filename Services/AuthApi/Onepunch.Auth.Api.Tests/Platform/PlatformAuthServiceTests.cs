using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Exceptions;
using Onepunch.Common.Lib.Platform;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

public class PlatformAuthServiceTests : IDisposable
{
    private readonly PlatformAuthHarness _h = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    public void Dispose() => _h.Dispose();

    private static JwtSecurityToken Read(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt);

    // ── Password step ────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithAnUnknownEmail_FailsWithTheSameGenericMessage()
    {
        await _h.SeedUserAsync();

        var unknown = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _h.NewService().LoginAsync("nobody@example.com", PlatformAuthHarness.StrongPassword, _ct));
        var wrongPassword = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _h.NewService().LoginAsync("ops@example.com", "wrong", _ct));

        unknown.Message.Should().Be(wrongPassword.Message, "the response must not reveal whether the email exists");
    }

    [Fact]
    public async Task Login_WithTheRightPassword_AsksToSetUpMfa_WhenNotEnrolledYet()
    {
        await _h.SeedUserAsync();

        var challenge = await _h.NewService().LoginAsync("OPS@example.com ", PlatformAuthHarness.StrongPassword, _ct);

        challenge.Step.Should().Be(PlatformLoginSteps.MfaSetup);
        challenge.ChallengeToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_NeverIssuesASession_OnThePasswordAlone()
    {
        await _h.SeedUserAsync();

        var challenge = await _h.NewService().LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct);

        // The challenge token carries the challenge audience, so it cannot call the platform API.
        Read(challenge.ChallengeToken).Audiences.Should().ContainSingle().Which.Should().Be(PlatformAuthDefaults.ChallengeAudience);
        Read(challenge.ChallengeToken).Claims.Should().NotContain(c => c.Type == PlatformAuthDefaults.PermissionClaim);
    }

    [Fact]
    public async Task FiveFailures_LockTheAccount_EvenForTheCorrectPassword_UntilTheLockoutPasses()
    {
        await _h.SeedUserAsync();
        for (var i = 0; i < _h.Settings.MaxFailedAccessAttempts; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                _h.NewService().LoginAsync("ops@example.com", "wrong", _ct));
        }

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _h.NewService().LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct));

        _h.Time.Advance(TimeSpan.FromMinutes(_h.Settings.LockoutMinutes + 1));
        var challenge = await _h.NewService().LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct);
        challenge.ChallengeToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ADeactivatedOperator_CannotSignIn()
    {
        var user = await _h.SeedUserAsync();
        await using (var db = _h.NewContext())
        {
            (await db.PlatformUsers.SingleAsync(x => x.Id == user.Id)).IsActive = false;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _h.NewService().LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct));
    }

    // ── MFA step ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MfaSetup_ReturnsTheSameSecretUntilItIsConfirmed()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        var challenge = await service.LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct);

        var first = await service.GetMfaSetupAsync(challenge.ChallengeToken, _ct);
        var second = await service.GetMfaSetupAsync(challenge.ChallengeToken, _ct);

        second.Secret.Should().Be(first.Secret);
        first.OtpAuthUri.Should().StartWith("otpauth://totp/").And.Contain(first.Secret);
    }

    [Fact]
    public async Task VerifyMfa_WithTheRightCode_OpensASessionWithThePermissionsOfTheRole()
    {
        await _h.SeedUserAsync(role: PlatformRoles.Billing);

        var session = await _h.SignInAsync(_h.NewService());

        var access = Read(session.AccessToken);
        access.Issuer.Should().Be(PlatformAuthDefaults.Issuer);
        access.Audiences.Should().ContainSingle().Which.Should().Be(PlatformAuthDefaults.Audience);
        access.Claims.Where(c => c.Type == PlatformAuthDefaults.PermissionClaim).Select(c => c.Value)
            .Should().BeEquivalentTo(PlatformRolePermissions.For(PlatformRoles.Billing));
        access.Claims.Should().Contain(c => c.Type == PlatformAuthDefaults.RoleClaim && c.Value == PlatformRoles.Billing);
        session.RefreshToken.Should().NotBeNullOrEmpty();
        session.User.Permissions.Should().NotContain(PlatformPermissions.AdminsManage);
    }

    [Fact]
    public async Task VerifyMfa_WithAWrongCode_Fails_AndCountsTowardsTheLockout()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        var challenge = await service.LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct);
        await service.GetMfaSetupAsync(challenge.ChallengeToken, _ct);

        for (var i = 0; i < _h.Settings.MaxFailedAccessAttempts; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                service.VerifyMfaAsync(challenge.ChallengeToken, "000000", _ct));
        }

        // Locked now: even the challenge itself no longer resolves.
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.GetMfaSetupAsync(challenge.ChallengeToken, _ct));
    }

    [Fact]
    public async Task VerifyMfa_RejectsAReplayedCode()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        await _h.SignInAsync(service);

        // Same 30-second step: signing in again with the same code must fail.
        var challenge = await service.LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct);
        challenge.Step.Should().Be(PlatformLoginSteps.MfaVerify);
        var secret = PlatformAuthHarness.Base32Decode(Totp.Base32Encode(Totp.NewSecret())); // unrelated secret, just to build a code
        var replay = Totp.ComputeCode(secret, Totp.StepOf(_h.Time.GetUtcNow()));

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.VerifyMfaAsync(challenge.ChallengeToken, replay, _ct));
    }

    [Fact]
    public async Task AnEnrolledOperator_IsAskedForTheCode_NotForSetup_OnTheNextSignIn()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        await _h.SignInAsync(service);
        _h.Time.Advance(TimeSpan.FromSeconds(31));

        var challenge = await service.LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct);

        challenge.Step.Should().Be(PlatformLoginSteps.MfaVerify);
        await Assert.ThrowsAsync<GuardException>(() => service.GetMfaSetupAsync(challenge.ChallengeToken, _ct));
    }

    [Fact]
    public async Task AnExpiredChallengeToken_IsRejected()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        var challenge = await service.LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct);

        _h.Time.Advance(TimeSpan.FromMinutes(_h.Settings.ChallengeMinutes + 1));

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.GetMfaSetupAsync(challenge.ChallengeToken, _ct));
    }

    // ── Sessions ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_RotatesTheToken_AndTheOldOneStopsWorking()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        var session = await _h.SignInAsync(service);

        _h.Time.Advance(TimeSpan.FromSeconds(_h.Settings.RefreshReuseGraceSeconds + 5));
        var refreshed = await service.RefreshAsync(session.RefreshToken, _ct);

        refreshed.RefreshToken.Should().NotBe(session.RefreshToken);
        refreshed.AccessToken.Should().NotBeNullOrEmpty();
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(session.RefreshToken, _ct));
    }

    [Fact]
    public async Task ReusingAnOldRefreshToken_AfterTheGraceWindow_EndsEverySessionOfThatOperator()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        var first = await _h.SignInAsync(service);
        _h.Time.Advance(TimeSpan.FromSeconds(_h.Settings.RefreshReuseGraceSeconds + 5));
        var second = await service.RefreshAsync(first.RefreshToken, _ct);

        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(first.RefreshToken, _ct)); // the stolen copy

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(second.RefreshToken, _ct)); // the legitimate one is gone too
    }

    [Fact]
    public async Task ARotatedTokenPresentedAgainWithinTheGraceWindow_IsRejectedWithoutKillingTheSession()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        var first = await _h.SignInAsync(service);
        var second = await service.RefreshAsync(first.RefreshToken, _ct);

        // Two tabs racing: the loser presents the token the winner just rotated.
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(first.RefreshToken, _ct));

        (await service.RefreshAsync(second.RefreshToken, _ct)).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AnExpiredRefreshToken_IsRejected()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        var session = await _h.SignInAsync(service);

        _h.Time.Advance(TimeSpan.FromDays(_h.Settings.RefreshDays + 1));

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(session.RefreshToken, _ct));
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        var session = await _h.SignInAsync(service);

        await service.LogoutAsync(session.RefreshToken, _ct);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(session.RefreshToken, _ct));
    }

    [Fact]
    public async Task DeactivatingAnOperator_StopsTheirSessionFromRefreshing()
    {
        var user = await _h.SeedUserAsync();
        var service = _h.NewService();
        var session = await _h.SignInAsync(service);
        await using (var db = _h.NewContext())
        {
            (await db.PlatformUsers.SingleAsync(x => x.Id == user.Id)).IsActive = false;
            await db.SaveChangesAsync();
        }
        _h.Time.Advance(TimeSpan.FromSeconds(_h.Settings.RefreshReuseGraceSeconds + 5));

        // A fresh service/context, as a new request would have -- the one above tracks the stale user.
        await Assert.ThrowsAsync<UnauthorizedException>(() => _h.NewService().RefreshAsync(session.RefreshToken, _ct));
    }

    // ── Forced password change ───────────────────────────────────────────────

    [Fact]
    public async Task AFirstSignIn_WithATemporaryPassword_GetsATokenWithNoPermissions()
    {
        await _h.SeedUserAsync(role: PlatformRoles.SuperAdmin, mustChangePassword: true);

        var session = await _h.SignInAsync(_h.NewService());

        session.User.MustChangePassword.Should().BeTrue();
        Read(session.AccessToken).Claims.Should().NotContain(c => c.Type == PlatformAuthDefaults.PermissionClaim,
            "every platform policy must fail closed until the password is changed");
        Read(session.AccessToken).Claims.Should().Contain(c => c.Type == "pwd_change" && c.Value == "required");
    }

    [Fact]
    public async Task ChangingThePassword_ClearsTheFlag_IssuesFullPermissions_AndEndsOldSessions()
    {
        await _h.SeedUserAsync(role: PlatformRoles.SuperAdmin, mustChangePassword: true);
        var service = _h.NewService();
        var temporary = await _h.SignInAsync(service);

        var changed = await service.ChangePasswordAsync(
            temporary.User.Id,
            new PlatformChangePasswordRequest(PlatformAuthHarness.StrongPassword, PlatformAuthHarness.NewStrongPassword), _ct);

        changed.User.MustChangePassword.Should().BeFalse();
        Read(changed.AccessToken).Claims.Where(c => c.Type == PlatformAuthDefaults.PermissionClaim)
            .Should().HaveCount(PlatformPermissions.All.Count);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(temporary.RefreshToken, _ct));

        // The old password no longer works; the new one does.
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct));
        (await service.LoginAsync("ops@example.com", PlatformAuthHarness.NewStrongPassword, _ct)).Step
            .Should().Be(PlatformLoginSteps.MfaVerify);
    }

    [Fact]
    public async Task ChangePassword_RejectsAWeakOrUnchangedPassword_AndAWrongCurrentOne()
    {
        var user = await _h.SeedUserAsync();
        var service = _h.NewService();

        await Assert.ThrowsAsync<GuardException>(() => service.ChangePasswordAsync(user.Id,
            new PlatformChangePasswordRequest(PlatformAuthHarness.StrongPassword, "weak"), _ct));
        await Assert.ThrowsAsync<GuardException>(() => service.ChangePasswordAsync(user.Id,
            new PlatformChangePasswordRequest(PlatformAuthHarness.StrongPassword, PlatformAuthHarness.StrongPassword), _ct));
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.ChangePasswordAsync(user.Id,
            new PlatformChangePasswordRequest("not-the-password", PlatformAuthHarness.NewStrongPassword), _ct));
    }

    // ── Accounts ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUser_RejectsADuplicateEmail_AnUnknownRole_AndAWeakPassword()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();

        await Assert.ThrowsAsync<GuardException>(() =>
            service.CreateUserAsync("OPS@example.com", "Dup", PlatformRoles.Support, PlatformAuthHarness.StrongPassword, "t", _ct));
        await Assert.ThrowsAsync<GuardException>(() =>
            service.CreateUserAsync("new@example.com", "New", "Owner", PlatformAuthHarness.StrongPassword, "t", _ct));
        await Assert.ThrowsAsync<GuardException>(() =>
            service.CreateUserAsync("new@example.com", "New", PlatformRoles.Support, "weak", "t", _ct));
    }

    [Fact]
    public async Task TheMfaSecret_IsNeverStoredInTheClear()
    {
        await _h.SeedUserAsync();
        var service = _h.NewService();
        var challenge = await service.LoginAsync("ops@example.com", PlatformAuthHarness.StrongPassword, _ct);
        var setup = await service.GetMfaSetupAsync(challenge.ChallengeToken, _ct);

        await using var db = _h.NewContext();
        var stored = (await db.PlatformUsers.SingleAsync()).MfaSecretProtected!;

        stored.Should().NotContain(setup.Secret);
    }
}
