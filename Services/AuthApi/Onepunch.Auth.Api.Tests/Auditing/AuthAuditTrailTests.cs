using BuzlinkRepository;
using FluentAssertions;
using MassTransit;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.AuditEngine;
using Onepunch.AuditEngine.MassTransit;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Cache;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Auditing;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.DTOs;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Auditing;

/// <summary>
/// Sign-in/sign-out audit events through the real UserService and the same registration AuthApi
/// uses (AddAuthAuditTrail): failures are recorded even though nothing else is saved, events
/// belong to the account's default company, and only company events are forwarded to hrms-api.
/// </summary>
public class AuthAuditTrailTests : IDisposable
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private readonly ServiceProvider _root;
    private readonly List<(AuditEventRecorded Message, string? CompanyHeader)> _forwarded = [];
    private readonly Mock<UserManager<User>> _userManager = IdentityMocks.MockUserManager();
    private readonly Mock<IPasswordHasher<User>> _passwordHasher = new();
    private readonly JwtService _jwtService = new(null!, null!, Options.Create(new JwtSettings
    {
        Issuer = "onepunch-auth",
        Audience = ["onepunch-clients"],
    }), null!, null!);

    public AuthAuditTrailTests()
    {
        var publisher = new Mock<IPublishEndpoint>();
        publisher
            .Setup(p => p.Publish(It.IsAny<AuditEventRecorded>(), It.IsAny<IPipe<PublishContext<AuditEventRecorded>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (AuditEventRecorded message, IPipe<PublishContext<AuditEventRecorded>> pipe, CancellationToken _) =>
            {
                string? header = null;
                var headers = new Mock<SendHeaders>();
                headers.Setup(h => h.Set(It.IsAny<string>(), It.IsAny<string>()))
                    .Callback((string key, string value) => { if (key == "X-Tenant-ID") header = value; });
                var context = new Mock<PublishContext<AuditEventRecorded>>();
                context.SetupGet(c => c.Headers).Returns(headers.Object);
                await pipe.Send(context.Object);
                _forwarded.Add((message, header));
            });

        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddScoped<ITenantProvider, FakeTenantProvider>();
        services.AddScoped(_ => publisher.Object);
        services.AddDbContext<AuthContext>(o => o
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddAuthAuditTrail(new ConfigurationBuilder().Build());
        _root = services.BuildServiceProvider();
    }

    public void Dispose() => _root.Dispose();

    private async Task<T> WithUserServiceAsync<T>(Func<UserService, AuthContext, Task<T>> action)
    {
        using var scope = _root.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthContext>();
        var service = new UserService(
            new UnitOfWorkService(context),
            new MembershipCacheService(Mock.Of<ICacheService>(), null!, null!),
            null!, null!, null!,
            _userManager.Object,
            null!, null!,
            _passwordHasher.Object,
            _jwtService,
            null!, null!, null!, null!, null!, null!,
            new NoOpMfaChallengeService(),
            Mock.Of<IMapper>(),
            scope.ServiceProvider.GetRequiredService<AuthAuditRecorder>());
        return await action(service, context);
    }

    private async Task<List<AuditEvent>> EventsAsync()
    {
        using var scope = _root.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IAuditQueryService>().QueryAsync(new AuditQuery { PageSize = 50 });
        return page.Items.ToList();
    }

    private static User Ana() => new()
    {
        Id = Guid.NewGuid(),
        Email = "ana@acme.com",
        UserName = "ana@acme.com",
        DefaultTenantId = CompanyId,
        Status = "Active",
        EmailConfirmed = true,
    };

    [Fact]
    public async Task SignInWithAnUnknownEmail_IsRecordedAsAFailure_ButBelongsToNoCompany()
    {
        var response = await WithUserServiceAsync((service, _) =>
            service.Login(new LoginPayload { Email = "ghost@acme.com", Password = "x" }, CancellationToken.None));

        response.ErrorMessage.Should().Be("Invalid email or password.", "the caller still sees the same generic message");
        var failed = (await EventsAsync()).Should().ContainSingle().Subject;
        failed.EventType.Should().Be(AuditEventTypes.Login);
        failed.Outcome.Should().Be(AuditOutcome.Failure);
        failed.OrganizationId.Should().BeNull();
        failed.Actor!.Type.Should().Be(AuditActorTypes.Anonymous);
        failed.Metadata[AuthAuditRecorder.AttemptedEmailKey].Should().Be("ghost@acme.com");
        _forwarded.Should().BeEmpty("no company's Security > Audit view owns it");
    }

    [Fact]
    public async Task WrongPassword_IsRecordedForTheAccountsCompany_AndForwarded()
    {
        var ana = Ana();
        _userManager.Setup(m => m.FindByEmailAsync(ana.Email!)).ReturnsAsync(ana);
        _passwordHasher.Setup(h => h.VerifyHashedPassword(ana, It.IsAny<string>(), "wrong")).Returns(PasswordVerificationResult.Failed);

        var response = await WithUserServiceAsync((service, _) =>
            service.Login(new LoginPayload { Email = ana.Email!, Password = "wrong" }, CancellationToken.None));

        response.ErrorMessage.Should().Be("Invalid email or password.");
        var failed = (await EventsAsync()).Should().ContainSingle().Subject;
        failed.Outcome.Should().Be(AuditOutcome.Failure);
        failed.OrganizationId.Should().Be(CompanyId.ToString());
        failed.Actor.Should().Be(AuditActor.User(ana.Id.ToString(), ana.Email));
        failed.Description.Should().Contain("Wrong password");
        _userManager.Verify(m => m.AccessFailedAsync(ana), Times.Once);
        _forwarded.Should().ContainSingle().Which.CompanyHeader.Should().Be(CompanyId.ToString());
    }

    [Fact]
    public async Task SigningOut_IsRecordedWithTheTokenRevocation()
    {
        var ana = Ana();
        _userManager.Setup(m => m.FindByIdAsync(ana.Id.ToString())).ReturnsAsync(ana);
        await WithUserServiceAsync(async (_, context) =>
        {
            context.RefreshTokens.Add(new RefreshToken
            {
                UserId = ana.Id,
                RefreshTokenHash = _jwtService.Hash("refresh-1"),
                Expiry = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
            return true;
        });

        await WithUserServiceAsync(async (service, _) =>
        {
            await service.RevokeRefreshTokenAsync("refresh-1", CancellationToken.None);
            return true;
        });

        var signedOut = (await EventsAsync()).Should().ContainSingle().Subject;
        signedOut.EventType.Should().Be(AuditEventTypes.Logout);
        signedOut.OrganizationId.Should().Be(CompanyId.ToString());
        signedOut.EntityId.Should().Be(ana.Id.ToString());
    }

    [Fact]
    public async Task SuccessfulSignIn_NamesTheUser_AndTheMethod()
    {
        var ana = Ana();
        using (var scope = _root.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthAuditRecorder>()
                .SignedInAsync(ana, AuthAuditRecorder.GoogleMethod, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<AuthContext>().SaveChangesAsync();
        }

        var signedIn = (await EventsAsync()).Should().ContainSingle().Subject;
        signedIn.Outcome.Should().Be(AuditOutcome.Success);
        signedIn.Actor.Should().Be(AuditActor.User(ana.Id.ToString(), ana.Email));
        signedIn.Metadata[AuthAuditRecorder.MethodKey].Should().Be(AuthAuditRecorder.GoogleMethod);
        signedIn.Source.Should().Be(AuthAuditRegistration.SourceName);
        signedIn.Hash.Should().NotBeNullOrEmpty();
    }
}
