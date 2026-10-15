using FluentAssertions;
using MapsterMapper;
using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.AuditEngine;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Portal;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Cache;
using Onepunch.Common.Lib.DTO;
using Onepunch.Common.Lib.Exceptions;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Auditing;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Portal;

/// <summary>
/// The Client Portal signs in through AuthApi but with a session of its own: a token no HRIS service
/// accepts, a refresh token the HRIS refresh refuses, and accounts the HRIS sign-in doesn't know.
/// </summary>
public class PortalAuthServiceTests : IDisposable
{
    private const string PortalUrl = "https://portal.example.com";
    private static readonly JwtSettings Jwt = new() { Issuer = "Onepunch", Audience = ["Onepunch.AuthService"] };

    private readonly Mock<UserManager<User>> _manager = IdentityMocks.MockUserManager();
    private readonly Mock<IPublishEndpoint> _publisher = new();
    private readonly AuthContext _context = AuthTestContextFactory.CreateContext();
    private readonly RsaKeyProvider _keys;
    private readonly string _keyFile = Path.Combine(Path.GetTempPath(), $"portal-test-{Guid.NewGuid():N}.key");
    private readonly PortalAuthSettings _settings = new() { PortalUrl = PortalUrl, ServiceKey = "svc-key" };
    private readonly User _ana = new()
    {
        Id = Guid.NewGuid(), Email = "ana@client.com", UserName = "ana@client.com", FullName = "Ana Cruz",
        Status = "Active", EmailConfirmed = true, PortalOnly = true,
    };

    public PortalAuthServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SigningKeyPath"] = _keyFile })
            .Build();
        _keys = new RsaKeyProvider(config, new EphemeralDataProtectionProvider());

        _manager.Setup(m => m.FindByEmailAsync(_ana.Email!)).ReturnsAsync(_ana);
        _manager.Setup(m => m.FindByIdAsync(_ana.Id.ToString())).ReturnsAsync(_ana);
        _manager.Setup(m => m.CheckPasswordAsync(_ana, "G00d!Passw0rd")).ReturnsAsync(true);
        _manager.Setup(m => m.IsLockedOutAsync(_ana)).ReturnsAsync(false);
        _manager.Setup(m => m.HasPasswordAsync(_ana)).ReturnsAsync(true);
        _manager.Setup(m => m.AccessFailedAsync(_ana)).ReturnsAsync(IdentityResult.Success);
        _manager.Setup(m => m.ResetAccessFailedCountAsync(_ana)).ReturnsAsync(IdentityResult.Success);
    }

    public void Dispose()
    {
        try { File.Delete(_keyFile); } catch (IOException) { }
    }

    private PortalAuthService Sut() => new(
        AuthTestContextFactory.CreateUnitOfWork(_context),
        _manager.Object,
        _keys,
        Options.Create(Jwt),
        Options.Create(_settings),
        _publisher.Object,
        TimeProvider.System,
        NullLogger<PortalAuthService>.Instance);

    private static async Task<UnauthorizedException> Unauthorized(Task task) =>
        (await Assert.ThrowsAsync<UnauthorizedException>(() => task));

    // ── Sign-in ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_IssuesAPortalTokenNoHrisServiceAccepts()
    {
        var session = await Sut().LoginAsync(" ana@client.com ", "G00d!Passw0rd", CancellationToken.None);

        session.AccountId.Should().Be(_ana.Id);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(session.AccessToken,
            PortalTokenValidation.AccessToken(_keys.SigningKey, "Onepunch", _settings.Audience), out _);
        principal.FindFirst("sub")!.Value.Should().Be(_ana.Id.ToString());
        principal.FindFirst("email")!.Value.Should().Be("ana@client.com");
        principal.FindFirst(PortalAuthService.UserTypeClaim)!.Value.Should().Be(PortalAuthService.UserTypeValue);
        // No tenant, role or permission claims: who the person is inside the portal is the portal's business.
        principal.Claims.Should().NotContain(c => c.Type == "tenantId" || c.Type == "permission" || c.Type == System.Security.Claims.ClaimTypes.Role);

        // The HRIS audience (what every other service validates for) rejects it.
        var hris = () => handler.ValidateToken(session.AccessToken,
            PortalTokenValidation.AccessToken(_keys.SigningKey, "Onepunch", "Onepunch.AuthService"), out _);
        hris.Should().Throw<Microsoft.IdentityModel.Tokens.SecurityTokenInvalidAudienceException>();
    }

    [Fact]
    public async Task Login_StoresOnlyTheRefreshTokensHash_TaggedForThePortal()
    {
        var session = await Sut().LoginAsync("ana@client.com", "G00d!Passw0rd", CancellationToken.None);

        var stored = await _context.RefreshTokens.SingleAsync();
        stored.RefreshTokenHash.Should().Be(PortalAuthService.HashToken(session.RefreshToken)).And.NotBe(session.RefreshToken);
        stored.Audience.Should().Be(RefreshToken.PortalAudience);
        stored.UserId.Should().Be(_ana.Id);
        session.RefreshTokenExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(_settings.RefreshDays), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Login_WrongPassword_CountsTheFailureAndSaysNothingAboutTheAccount()
    {
        var ex = await Unauthorized(Sut().LoginAsync("ana@client.com", "nope", CancellationToken.None));

        ex.Message.Should().Be("Invalid email or password.");
        _manager.Verify(m => m.AccessFailedAsync(_ana), Times.Once);
        (await _context.RefreshTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Login_UnknownEmail_GivesTheSameAnswerAsAWrongPassword()
    {
        var ex = await Unauthorized(Sut().LoginAsync("ghost@client.com", "x", CancellationToken.None));

        ex.Message.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Login_LockedAccount_IsRefusedWithoutCheckingThePassword()
    {
        _manager.Setup(m => m.IsLockedOutAsync(_ana)).ReturnsAsync(true);

        var ex = await Unauthorized(Sut().LoginAsync("ana@client.com", "G00d!Passw0rd", CancellationToken.None));

        ex.Message.Should().Contain("locked");
        _manager.Verify(m => m.CheckPasswordAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Login_AFailureThatTripsTheLockoutSaysSo()
    {
        _manager.SetupSequence(m => m.IsLockedOutAsync(_ana)).ReturnsAsync(false).ReturnsAsync(true);

        var ex = await Unauthorized(Sut().LoginAsync("ana@client.com", "nope", CancellationToken.None));

        ex.Message.Should().Contain("locked");
    }

    [Theory]
    [InlineData("Pending", true)]
    [InlineData("Active", false)]
    public async Task Login_AnAccountThatIsNotActiveOrConfirmed_IsRefused(string status, bool confirmed)
    {
        _ana.Status = status;
        _ana.EmailConfirmed = confirmed;

        await Unauthorized(Sut().LoginAsync("ana@client.com", "G00d!Passw0rd", CancellationToken.None));
    }

    // ── Refresh / logout ─────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_RotatesTheTokenAndAnOldOneNoLongerWorks()
    {
        var first = await Sut().LoginAsync("ana@client.com", "G00d!Passw0rd", CancellationToken.None);

        var second = await Sut().RefreshAsync(first.RefreshToken, CancellationToken.None);

        second.RefreshToken.Should().NotBe(first.RefreshToken);
        second.AccountId.Should().Be(_ana.Id);
        await Unauthorized(Sut().RefreshAsync(first.RefreshToken, CancellationToken.None));
        // The new one still works.
        (await Sut().RefreshAsync(second.RefreshToken, CancellationToken.None)).AccountId.Should().Be(_ana.Id);
    }

    [Fact]
    public async Task Refresh_RefusesAnHrisSessionsToken()
    {
        const string hrisToken = "an-hris-refresh-token";
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = _ana.Id, RefreshTokenHash = PortalAuthService.HashToken(hrisToken),
            Expiry = DateTime.UtcNow.AddDays(1), CreatedAt = DateTime.UtcNow, Audience = null,
        });
        await _context.SaveChangesAsync();

        await Unauthorized(Sut().RefreshAsync(hrisToken, CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_RefusesAnExpiredTokenAndNothing()
    {
        var session = await Sut().LoginAsync("ana@client.com", "G00d!Passw0rd", CancellationToken.None);
        var stored = await _context.RefreshTokens.SingleAsync();
        stored.Expiry = DateTime.UtcNow.AddMinutes(-1);
        await _context.SaveChangesAsync();

        await Unauthorized(Sut().RefreshAsync(session.RefreshToken, CancellationToken.None));
        await Unauthorized(Sut().RefreshAsync(null, CancellationToken.None));
        await Unauthorized(Sut().RefreshAsync("never-issued", CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_ForAnAccountLockedSinceTheSignIn_RevokesTheSession()
    {
        var session = await Sut().LoginAsync("ana@client.com", "G00d!Passw0rd", CancellationToken.None);
        _manager.Setup(m => m.IsLockedOutAsync(_ana)).ReturnsAsync(true);

        await Unauthorized(Sut().RefreshAsync(session.RefreshToken, CancellationToken.None));

        (await _context.RefreshTokens.SingleAsync()).Revoked.Should().BeTrue();
    }

    [Fact]
    public async Task Logout_RevokesTheSession()
    {
        var session = await Sut().LoginAsync("ana@client.com", "G00d!Passw0rd", CancellationToken.None);

        await Sut().LogoutAsync(session.RefreshToken, CancellationToken.None);

        (await _context.RefreshTokens.SingleAsync()).Revoked.Should().BeTrue();
        await Unauthorized(Sut().RefreshAsync(session.RefreshToken, CancellationToken.None));
        await Sut().LogoutAsync(null, CancellationToken.None);
    }

    // ── Password reset ───────────────────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_SendsALinkIntoThePortal()
    {
        _manager.Setup(m => m.GeneratePasswordResetTokenAsync(_ana)).ReturnsAsync("identity-reset-token");
        ResetPasswordEmail? sent = null;
        _publisher.Setup(p => p.Publish(It.IsAny<ResetPasswordEmail>(), It.IsAny<CancellationToken>()))
            .Callback<ResetPasswordEmail, CancellationToken>((m, _) => sent = m).Returns(Task.CompletedTask);

        await Sut().ForgotPasswordAsync("ana@client.com", CancellationToken.None);

        sent.Should().NotBeNull();
        sent!.Email.Should().Be("ana@client.com");
        sent.ResetLink.Should().StartWith($"{PortalUrl}/reset-password?token=").And.EndWith("&email=ana%40client.com");
        Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(sent.Token)).Should().Be("identity-reset-token");
    }

    [Fact]
    public async Task ForgotPassword_SaysNothingForAnUnknownEmailOrAnAccountWithoutAPassword()
    {
        _manager.Setup(m => m.HasPasswordAsync(_ana)).ReturnsAsync(false);

        await Sut().ForgotPasswordAsync("ghost@client.com", CancellationToken.None);
        await Sut().ForgotPasswordAsync("ana@client.com", CancellationToken.None);
        await Sut().ForgotPasswordAsync("", CancellationToken.None);

        _publisher.Verify(p => p.Publish(It.IsAny<ResetPasswordEmail>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_WithoutAPortalAddressSendsNothing()
    {
        _settings.PortalUrl = "";

        await Sut().ForgotPasswordAsync("ana@client.com", CancellationToken.None);

        _publisher.Verify(p => p.Publish(It.IsAny<ResetPasswordEmail>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResetPassword_SetsThePasswordAndEndsEverySession()
    {
        var session = await Sut().LoginAsync("ana@client.com", "G00d!Passw0rd", CancellationToken.None);
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("identity-reset-token"));
        _manager.Setup(m => m.ResetPasswordAsync(_ana, "identity-reset-token", "N3w!Passw0rd")).ReturnsAsync(IdentityResult.Success);

        await Sut().ResetPasswordAsync("ana@client.com", token, "N3w!Passw0rd", CancellationToken.None);

        (await _context.RefreshTokens.SingleAsync()).Revoked.Should().BeTrue();
        await Unauthorized(Sut().RefreshAsync(session.RefreshToken, CancellationToken.None));
    }

    [Fact]
    public async Task ResetPassword_ABadLinkIsRefusedWithoutSayingWhy()
    {
        _manager.Setup(m => m.ResetPasswordAsync(_ana, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "Invalid token." }));
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("wrong"));

        var act = () => Sut().ResetPasswordAsync("ana@client.com", token, "N3w!Passw0rd", CancellationToken.None);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("This reset link is invalid or has expired.");
        var unknown = () => Sut().ResetPasswordAsync("ghost@client.com", token, "N3w!Passw0rd", CancellationToken.None);
        await unknown.Should().ThrowAsync<GuardException>();
        var garbage = () => Sut().ResetPasswordAsync("ana@client.com", "***not base64***", "N3w!Passw0rd", CancellationToken.None);
        await garbage.Should().ThrowAsync<GuardException>();
    }

    [Fact]
    public async Task ResetPassword_AWeakPasswordSaysWhatIsWrongWithIt()
    {
        _manager.Setup(m => m.ResetPasswordAsync(_ana, It.IsAny<string>(), "weak"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 6 characters." }));
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("t"));

        var act = () => Sut().ResetPasswordAsync("ana@client.com", token, "weak", CancellationToken.None);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*at least 6 characters*");
    }

    // ── Accounts ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureAccount_CreatesAPortalOnlyLogin()
    {
        User? created = null;
        _manager.Setup(m => m.FindByEmailAsync("new@client.com")).ReturnsAsync((User?)null);
        _manager.Setup(m => m.CreateAsync(It.IsAny<User>(), "G00d!Passw0rd"))
            .Callback<User, string>((u, _) => created = u).ReturnsAsync(IdentityResult.Success);

        var result = await Sut().EnsureAccountAsync(new PortalEnsureAccountRequest("new@client.com", " Ben Reyes ", "G00d!Passw0rd"), CancellationToken.None);

        result.Created.Should().BeTrue();
        result.AccountId.Should().Be(created!.Id);
        created.PortalOnly.Should().BeTrue();
        created.EmailConfirmed.Should().BeTrue();
        created.Status.Should().Be("Active");
        created.FullName.Should().Be("Ben Reyes");
    }

    [Fact]
    public async Task EnsureAccount_LeavesAnExistingAccountAlone()
    {
        var result = await Sut().EnsureAccountAsync(new PortalEnsureAccountRequest("ana@client.com", "Someone Else", "An0ther!Passw0rd"), CancellationToken.None);

        result.Should().Be(new PortalEnsureAccountResult(_ana.Id, false));
        _manager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("not-an-email", "Ben", "G00d!Passw0rd")]
    [InlineData("two@a.com, three@b.com", "Ben", "G00d!Passw0rd")]
    [InlineData("new@client.com", "", "G00d!Passw0rd")]
    [InlineData("new@client.com", "Ben", "")]
    public async Task EnsureAccount_RefusesIncompleteRequests(string email, string name, string password)
    {
        _manager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        var act = () => Sut().EnsureAccountAsync(new PortalEnsureAccountRequest(email, name, password), CancellationToken.None);

        await act.Should().ThrowAsync<GuardException>();
        _manager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureAccount_ReportsPasswordPolicyFailures()
    {
        _manager.Setup(m => m.FindByEmailAsync("new@client.com")).ReturnsAsync((User?)null);
        _manager.Setup(m => m.CreateAsync(It.IsAny<User>(), "weak"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Passwords must have at least one digit." }));

        var act = () => Sut().EnsureAccountAsync(new PortalEnsureAccountRequest("new@client.com", "Ben", "weak"), CancellationToken.None);

        (await act.Should().ThrowAsync<GuardException>()).WithMessage("*at least one digit*");
    }

    [Fact]
    public void TheWireShape_IsWhatTheClientPortalReads()
    {
        // The portal's AuthApiIdentityProvider reads these property names; renaming one here breaks sign-in there.
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var session = System.Text.Json.JsonSerializer.SerializeToElement(
            new PortalSession("a", DateTime.UtcNow, "r", DateTime.UtcNow, Guid.NewGuid(), "e@x.test", "Name"), options);
        session.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "accessToken", "accessTokenExpiresAtUtc", "refreshToken", "refreshTokenExpiresAtUtc", "accountId", "email", "fullName");

        var account = System.Text.Json.JsonSerializer.SerializeToElement(new PortalEnsureAccountResult(Guid.NewGuid(), true), options);
        account.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("accountId", "created");
    }

    /// <summary>Runs the controller's guard (the filter that runs before every action) the way MVC would, with the given key header.</summary>
    private Action GuardWith(string? presented)
    {
        var controller = new OnePunch.Auth.Api.Controllers.PortalAuthController(Sut(), Options.Create(_settings));
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        if (!string.IsNullOrEmpty(presented)) http.Request.Headers[PortalAuthSettings.KeyHeader] = presented;
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            http, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = http };
        var executing = new Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext(
            actionContext, new List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>(), new Dictionary<string, object?>(), controller);
        return () => controller.OnActionExecuting(executing);
    }

    [Theory]
    [InlineData("svc-key", true)]
    [InlineData("wrong", false)]
    [InlineData("svc-key-and-more", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EveryPortalEndpoint_AnswersOnlyToThePortalsServer(string? presented, bool allowed)
    {
        var act = GuardWith(presented);

        if (allowed) act.Should().NotThrow();
        else act.Should().Throw<UnauthorizedException>();
    }

    [Fact]
    public void WithNoServiceKeyConfigured_NobodyIsLetIn()
    {
        _settings.ServiceKey = "";

        GuardWith("").Should().Throw<UnauthorizedException>();
        GuardWith(null).Should().Throw<UnauthorizedException>();
    }

    [Theory]
    [InlineData("svc-key", "svc-key", true)]
    [InlineData("wrong", "svc-key", false)]
    [InlineData("", "svc-key", false)]
    [InlineData(null, "svc-key", false)]
    [InlineData("anything", "", false)]
    public void ServiceKey_MatchesOnlyTheConfiguredSecret(string? presented, string expected, bool matches) =>
        PortalTokenValidation.KeyMatches(presented, expected).Should().Be(matches);

    // ── The HRIS side doesn't take portal sessions or accounts ───────────────

    private UserService HrisUserService() => new(
        AuthTestContextFactory.CreateUnitOfWork(_context),
        new MembershipCacheService(Mock.Of<ICacheService>(), null!, null!),
        null!, null!, null!,
        _manager.Object,
        null!, null!,
        Mock.Of<IPasswordHasher<User>>(),
        new JwtService(null!, null!, Options.Create(Jwt), null!, null!),
        null!, null!, null!, null!, null!, null!,
        new NoOpMfaChallengeService(),
        Mock.Of<IMapper>(),
        new AuthAuditRecorder(Mock.Of<IAuditWriter>()));

    [Fact]
    public async Task HrisRefresh_RefusesAPortalSessionsToken()
    {
        var session = await Sut().LoginAsync("ana@client.com", "G00d!Passw0rd", CancellationToken.None);

        var response = await HrisUserService().RefreshLogin(session.RefreshToken, CancellationToken.None);

        response.AccessToken.Should().BeNullOrEmpty();
        response.ErrorMessage.Should().Be("Invalid or expired refresh token.");
        (await _context.RefreshTokens.SingleAsync()).Revoked.Should().BeFalse();
    }

    [Fact]
    public async Task HrisLogin_DoesNotKnowAPortalOnlyAccount()
    {
        var response = await HrisUserService().Login(new LoginPayload { Email = "ana@client.com", Password = "G00d!Passw0rd" }, CancellationToken.None);

        response.AccessToken.Should().BeNullOrEmpty();
        response.ErrorMessage.Should().Be("Invalid email or password.");
        _manager.Verify(m => m.AccessFailedAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task HrisPasswordReset_IgnoresAPortalOnlyAccount()
    {
        await HrisUserService().ResetPasswordRequestAsync("ana@client.com", "https://hris.example.com", CancellationToken.None);

        _manager.Verify(m => m.GeneratePasswordResetTokenAsync(It.IsAny<User>()), Times.Never);
    }
}
