using FluentAssertions;
using MapsterMapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Cache;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

/// <summary>
/// A token refresh keeps the session in the company it was working in -- not the account-wide
/// default company, which switching company on another device moves -- unless the user no longer
/// belongs there. Sessions from before refresh tokens remembered their company refresh into the
/// default, as before.
/// </summary>
public class SessionTenantRefreshTests : IDisposable
{
    private static readonly Guid HomeCo = Guid.NewGuid();
    private static readonly Guid BranchCo = Guid.NewGuid();

    private static UsersTenant Membership(Guid tenantId, string name, params string[] roles) => new()
    {
        TenantId = tenantId,
        Name = name,
        Roles = [.. roles],
        Permissions = [$"{name}:View"],
        State = "Active",
    };

    // ---- SessionTenantResolver ------------------------------------------------------------

    [Fact]
    public void Resolve_KeepsTheSessionsCompany_WithItsCurrentName()
    {
        var session = SessionTenantResolver.Resolve(
            BranchCo, "Branch Old Name", HomeCo, "Home Co",
            [Membership(HomeCo, "Home Co"), Membership(BranchCo, "Branch Co")], ownCreationPending: false);

        session.Should().Be(new SessionTenant(BranchCo, "Branch Co", false));
    }

    [Fact]
    public void Resolve_FallsBackToTheDefault_WhenTheUserLeftTheSessionsCompany()
    {
        var session = SessionTenantResolver.Resolve(
            BranchCo, "Branch Co", HomeCo, "Home Co", [Membership(HomeCo, "Home Co")], ownCreationPending: false);

        session.Should().Be(new SessionTenant(HomeCo, "Home Co", false));
    }

    [Fact]
    public void Resolve_UsesTheDefault_ForATokenWithoutACompany()
    {
        var session = SessionTenantResolver.Resolve(
            null, null, HomeCo, "Home Co", [Membership(HomeCo, "Home Co"), Membership(BranchCo, "Branch Co")], false);

        session.TenantId.Should().Be(HomeCo);
    }

    [Fact]
    public void Resolve_KeepsACompanyTheUserIsStillCreating()
    {
        var session = SessionTenantResolver.Resolve(
            BranchCo, "New Co", HomeCo, "Home Co", [Membership(HomeCo, "Home Co")], ownCreationPending: true);

        session.Should().Be(new SessionTenant(BranchCo, "New Co", true));
    }

    [Fact]
    public void Resolve_HasNoCompany_WhenThereIsNoDefaultEither()
    {
        var session = SessionTenantResolver.Resolve(Guid.Empty, null, null, null, [], false);

        session.TenantId.Should().BeNull();
    }

    // ---- RefreshLogin, end to end ---------------------------------------------------------

    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"jwt-test-signing-{Guid.NewGuid()}.key");
    private readonly string _dpDirectory = Path.Combine(Path.GetTempPath(), $"jwt-test-dp-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (File.Exists(_keyPath)) File.Delete(_keyPath);
        if (Directory.Exists(_dpDirectory)) Directory.Delete(_dpDirectory, recursive: true);
    }

    private (UserService Service, AuthContext Context, JwtService Jwt) CreateSut(List<UsersTenant> memberships)
    {
        var context = AuthTestContextFactory.CreateContext();
        var uow = new UnitOfWorkService(context);
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<List<UsersTenant>>(It.IsAny<string>())).ReturnsAsync(memberships);
        var membershipCache = new MembershipCacheService(cache.Object, uow, null!);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SigningKeyPath"] = _keyPath })
            .Build();
        var rsaKeyProvider = new RsaKeyProvider(configuration, DataProtectionProvider.Create(new DirectoryInfo(_dpDirectory)));
        var jwt = new JwtService(null!, new TenantRequestService(uow, configuration), Options.Create(new JwtSettings
        {
            Issuer = "onepunch-auth",
            Audience = ["onepunch-clients"],
            TokenExpiry = 10,
            RefreshExpiry = 7,
        }), rsaKeyProvider, membershipCache);

        var service = new UserService(
            uow, membershipCache,
            null!, null!, null!,
            IdentityMocks.MockUserManager().Object,
            null!, null!,
            Mock.Of<IPasswordHasher<User>>(),
            jwt,
            null!, null!, null!, null!, null!, null!,
            new NoOpMfaChallengeService(),
            Mock.Of<IMapper>(),
            null!);
        return (service, context, jwt);
    }

    private static User Ana() => new()
    {
        Id = Guid.NewGuid(),
        Email = "ana@acme.com",
        UserName = "ana@acme.com",
        Status = "Active",
        EmailConfirmed = true,
        // Ana switched to Home Co on her phone, so that's the account-wide default now...
        DefaultTenantId = HomeCo,
        DefaultTenantName = "Home Co",
    };

    private static async Task SeedSessionAsync(AuthContext context, JwtService jwt, User user, Guid? tenantId, string? tenantName)
    {
        context.Users.Add(user);
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            RefreshTokenHash = jwt.Hash("laptop-refresh"),
            Expiry = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow,
            TenantId = tenantId,
            TenantName = tenantName,
        });
        await context.SaveChangesAsync();
    }

    private static string TenantClaim(string accessToken) =>
        new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.Single(c => c.Type == "tenantId").Value;

    [Fact]
    public async Task Refresh_StaysInTheCompanyTheSessionWasWorkingIn()
    {
        var (service, context, jwt) = CreateSut(
            [Membership(HomeCo, "Home Co", "Employee"), Membership(BranchCo, "Branch Co", "HR")]);
        var ana = Ana();
        // ...but her laptop is working in Branch Co.
        await SeedSessionAsync(context, jwt, ana, BranchCo, "Branch Co");

        var response = await service.RefreshLogin("laptop-refresh", CancellationToken.None);

        response.ErrorMessage.Should().BeNullOrEmpty();
        TenantClaim(response.AccessToken!).Should().Be(BranchCo.ToString());
        response.Roles.Should().Equal("HR");
        response.Permissions.Should().Equal("Branch Co:View");
        var next = await context.RefreshTokens.AsNoTracking().SingleAsync(t => !t.Revoked);
        next.TenantId.Should().Be(BranchCo, "the next refresh stays there too");
    }

    [Fact]
    public async Task Refresh_MovesToTheDefault_WhenTheUserNoLongerBelongsToTheSessionsCompany()
    {
        var (service, context, jwt) = CreateSut([Membership(HomeCo, "Home Co", "Employee")]);
        var ana = Ana();
        await SeedSessionAsync(context, jwt, ana, BranchCo, "Branch Co");

        var response = await service.RefreshLogin("laptop-refresh", CancellationToken.None);

        TenantClaim(response.AccessToken!).Should().Be(HomeCo.ToString());
        response.Roles.Should().Equal("Employee");
    }

    [Fact]
    public async Task Refresh_OfAnOlderSessionWithoutACompany_UsesTheDefault()
    {
        var (service, context, jwt) = CreateSut(
            [Membership(HomeCo, "Home Co", "Employee"), Membership(BranchCo, "Branch Co", "HR")]);
        var ana = Ana();
        await SeedSessionAsync(context, jwt, ana, null, null);

        var response = await service.RefreshLogin("laptop-refresh", CancellationToken.None);

        TenantClaim(response.AccessToken!).Should().Be(HomeCo.ToString());
        var next = await context.RefreshTokens.AsNoTracking().SingleAsync(t => !t.Revoked);
        next.TenantId.Should().Be(HomeCo, "from now on the session remembers its company");
    }

    [Fact]
    public async Task Refresh_KeepsACompanyTheUserIsStillCreating_AsItsOwner()
    {
        var (service, context, jwt) = CreateSut([Membership(HomeCo, "Home Co", "Employee")]);
        var ana = Ana();
        context.TenantCreationRequests.Add(new TenantCreationRequestStatus
        {
            TenantId = BranchCo,
            UserId = ana.Id,
            TenantName = "New Co",
            Status = TenantCreationStatus.Provisioning,
        });
        await SeedSessionAsync(context, jwt, ana, BranchCo, "New Co");

        var response = await service.RefreshLogin("laptop-refresh", CancellationToken.None);

        TenantClaim(response.AccessToken!).Should().Be(BranchCo.ToString());
        response.Roles.Should().Equal("Owner");
    }
}
