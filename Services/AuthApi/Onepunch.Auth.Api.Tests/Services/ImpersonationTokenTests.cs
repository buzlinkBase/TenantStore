using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Cache;
using Onepunch.Common.Lib.Security;
using OnePunch.Auth.Core;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Services;

public class ImpersonationTokenTests : IDisposable
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), "imp-key-" + Guid.NewGuid().ToString("N") + ".key");
    private readonly string _dpDirectory = Path.Combine(Path.GetTempPath(), "imp-dp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (File.Exists(_keyPath)) File.Delete(_keyPath);
        if (Directory.Exists(_dpDirectory)) Directory.Delete(_dpDirectory, recursive: true);
    }

    private JwtService CreateJwt()
    {
        var context = AuthTestContextFactory.CreateContext();
        var uow = new UnitOfWorkService(context);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SigningKeyPath"] = _keyPath })
            .Build();
        var keys = new RsaKeyProvider(configuration, DataProtectionProvider.Create(new DirectoryInfo(_dpDirectory)));
        var membershipCache = new MembershipCacheService(Mock.Of<ICacheService>(), uow, null!);
        return new JwtService(null!, new TenantRequestService(uow, configuration), Options.Create(new JwtSettings
        {
            Issuer = "onepunch-auth",
            Audience = ["onepunch-clients"],
            TokenExpiry = 5,
            ImpersonationMaxMinutes = 30,
        }), keys, membershipCache);
    }

    [Fact]
    public async Task TheToken_IsTheTargetUsers_TaggedWithTheOperator_AndExpiresWithTheSession()
    {
        var jwt = CreateJwt();
        var target = new User { Id = Guid.NewGuid(), Email = "ann@acme.test" };
        var tenantId = Guid.NewGuid();
        var session = new ImpersonationSession
        {
            OperatorId = Guid.NewGuid(),
            OperatorEmail = "ops@platform.test",
            OperatorRole = "Support",
            // As read back from MySQL: no DateTimeKind.
            ExpiresAtUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(30), DateTimeKind.Unspecified),
        };

        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            await jwt.CreateImpersonationTokenAsync(target, tenantId, "Acme", ["Employee"], ["Leave:View"], session));

        token.Claims.First(c => c.Type == "sub").Value.Should().Be(target.Id.ToString());
        token.Claims.First(c => c.Type == "tenantId").Value.Should().Be(tenantId.ToString());
        token.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role").Select(c => c.Value).Should().Contain("Employee");
        token.Claims.Where(c => c.Type == "permission").Select(c => c.Value).Should().Contain("Leave:View");
        token.Claims.First(c => c.Type == ImpersonationClaims.Marker).Value.Should().Be("1");
        token.Claims.First(c => c.Type == ImpersonationClaims.ActorId).Value.Should().Be(session.OperatorId.ToString());
        token.Claims.First(c => c.Type == ImpersonationClaims.ActorEmail).Value.Should().Be("ops@platform.test");
        token.Claims.First(c => c.Type == ImpersonationClaims.SessionId).Value.Should().Be(session.Id.ToString());
        token.ValidTo.Should().BeCloseTo(DateTime.SpecifyKind(session.ExpiresAtUtc, DateTimeKind.Utc), TimeSpan.FromSeconds(2));
    }
}
