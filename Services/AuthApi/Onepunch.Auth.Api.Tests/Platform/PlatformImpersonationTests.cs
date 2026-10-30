using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Cache;
using Onepunch.Common.Lib.Exceptions;
using Onepunch.Common.Lib.Platform;
using Onepunch.Common.Lib.Security;
using Onepunch.Common.Web.Authorization;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Platform;

public class ImpersonationPolicyTests
{
    [Theory]
    [InlineData(PlatformRoles.SuperAdmin, "Owner", true)]
    [InlineData(PlatformRoles.SuperAdmin, "Admin", true)]
    [InlineData(PlatformRoles.SuperAdmin, "Employee", true)]
    [InlineData(PlatformRoles.Support, "Owner", false)]
    [InlineData(PlatformRoles.Support, "Admin", false)]
    [InlineData(PlatformRoles.Support, "owner", false)]
    [InlineData(PlatformRoles.Support, "Employee", true)]
    [InlineData(PlatformRoles.Support, "Payroll Clerk", true)]
    [InlineData(PlatformRoles.Billing, "Employee", false)]
    [InlineData("Unknown", "Employee", false)]
    [InlineData(null, "Employee", false)]
    public void CanImpersonate_FollowsTheOperatorRolesCeiling(string? operatorRole, string targetRole, bool expected) =>
        ImpersonationPolicy.CanImpersonate(operatorRole, [targetRole]).Should().Be(expected);

    [Fact]
    public void CanImpersonate_RefusesWhenAnyOfTheTargetsRolesIsAboveTheCeiling() =>
        ImpersonationPolicy.CanImpersonate(PlatformRoles.Support, ["Employee", "Admin"]).Should().BeFalse();

    [Fact]
    public void OnlySuperAdminAndSupportHoldThePermission()
    {
        PlatformRolePermissions.For(PlatformRoles.SuperAdmin).Should().Contain(PlatformPermissions.ImpersonateUsers);
        PlatformRolePermissions.For(PlatformRoles.Support).Should().Contain(PlatformPermissions.ImpersonateUsers);
        PlatformRolePermissions.For(PlatformRoles.Billing).Should().NotContain(PlatformPermissions.ImpersonateUsers);
    }
}

public class ImpersonationClaimsTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));

    [Fact]
    public void ARegularUser_IsNotImpersonating()
    {
        var user = Principal(new Claim("sub", "u1"));
        user.IsImpersonating().Should().BeFalse();
        user.GetActorId().Should().BeNull();
        user.GetImpersonationSessionId().Should().BeNull();
    }

    [Fact]
    public void AnImpersonationToken_ExposesTheOperatorAndSession()
    {
        var session = Guid.NewGuid();
        var user = Principal(
            new Claim("sub", "u1"), new Claim(ImpersonationClaims.Marker, "1"),
            new Claim(ImpersonationClaims.ActorId, "op1"), new Claim(ImpersonationClaims.SessionId, session.ToString()));

        user.IsImpersonating().Should().BeTrue();
        user.GetActorId().Should().Be("op1");
        user.GetImpersonationSessionId().Should().Be(session);
    }

    [Fact]
    public void ActorClaimsWithoutTheMarker_AreIgnored() =>
        Principal(new Claim(ImpersonationClaims.ActorId, "op1")).GetActorId().Should().BeNull();
}

public class ImpersonationGuardTests
{
    private static DefaultHttpContext Context(string method, params Claim[] claims) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
        Request = { Method = method },
    };

    private static Claim[] Impersonating(Guid session) =>
        [new Claim("sub", "u1"), new Claim(ImpersonationClaims.Marker, "1"), new Claim(ImpersonationClaims.SessionId, session.ToString())];

    [Fact]
    public async Task ASessionThatWasStopped_IsRefusedAtOnce()
    {
        var session = Guid.NewGuid();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<bool>(ImpersonationClaims.EndedCacheKey(session))).ReturnsAsync(true);
        var reached = false;
        var middleware = new ImpersonationSessionMiddleware(_ => { reached = true; return Task.CompletedTask; });
        var context = Context("GET", Impersonating(session));

        await middleware.InvokeAsync(context, cache.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        reached.Should().BeFalse();
    }

    [Fact]
    public async Task ALiveSession_AndARegularUser_PassThrough()
    {
        var cache = new Mock<ICacheService>();
        var calls = 0;
        var middleware = new ImpersonationSessionMiddleware(_ => { calls++; return Task.CompletedTask; });

        await middleware.InvokeAsync(Context("GET", Impersonating(Guid.NewGuid())), cache.Object);
        await middleware.InvokeAsync(Context("GET", new Claim("sub", "u1")), cache.Object);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task AnImpersonationTokenWithoutASessionId_IsRefused()
    {
        var middleware = new ImpersonationSessionMiddleware(_ => Task.CompletedTask);
        var context = Context("GET", new Claim("sub", "u1"), new Claim(ImpersonationClaims.Marker, "1"));

        await middleware.InvokeAsync(context, Mock.Of<ICacheService>());

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    [InlineData("POST", false)]
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    public async Task ReadOnlyGuard_LetsAnImpersonatorReadButNotChange(string method, bool allowed)
    {
        var reached = false;
        var middleware = new ImpersonationReadOnlyMiddleware(_ => { reached = true; return Task.CompletedTask; });
        var context = Context(method, Impersonating(Guid.NewGuid()));

        await middleware.InvokeAsync(context);

        reached.Should().Be(allowed);
        if (!allowed) context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task ReadOnlyGuard_DoesNotTouchARegularUsersWrites()
    {
        var reached = false;
        var middleware = new ImpersonationReadOnlyMiddleware(_ => { reached = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(Context("POST", new Claim("sub", "u1")));

        reached.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ForbidWhenImpersonating_RefusesOnlyAnImpersonationSession(bool impersonating, bool refused)
    {
        var http = Context("POST", impersonating
            ? Impersonating(Guid.NewGuid())
            : [new Claim("sub", "u1")]);
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), [], new Dictionary<string, object?>(), new object());

        new ForbidWhenImpersonatingAttribute().OnActionExecuting(context);

        (context.Result is ObjectResult { StatusCode: StatusCodes.Status403Forbidden }).Should().Be(refused);
    }
}

public class PlatformImpersonationServiceTests : IDisposable
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly PlatformAuthHarness _h = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public void Dispose() => _h.Dispose();

    private sealed record Rig(
        PlatformImpersonationService Sut, PlatformAuthContext Db, Mock<ICacheService> Cache,
        Mock<JwtService> Jwt, User Target, PlatformUser Operator);

    private async Task<Rig> CreateRigAsync(string operatorRole, params string[] targetRoles)
    {
        var op = await _h.SeedUserAsync("ops@platform.test", operatorRole);
        var target = new User { Id = Guid.NewGuid(), Email = "ann@acme.test", FullName = "Ann Acme" };

        var users = IdentityMocks.MockUserManager();
        users.Setup(u => u.FindByIdAsync(target.Id.ToString())).ReturnsAsync(target);

        var membership = new UsersTenant { TenantId = _tenantId, Name = "Acme", Roles = [.. targetRoles], Permissions = ["Leave:View"] };
        var memberships = new Mock<MembershipCacheService>(null!, null!, null!);
        memberships.Setup(m => m.GetMembershipsAsync(target.Id, It.IsAny<int>())).ReturnsAsync([membership]);

        var jwt = new Mock<JwtService>(null!, null!, Microsoft.Extensions.Options.Options.Create(new Onepunch.Common.Lib.JwtSettings()), null!, null!);
        jwt.Setup(j => j.ImpersonationMaxMinutes).Returns(30);
        jwt.Setup(j => j.CreateImpersonationTokenAsync(
                target, _tenantId, "Acme", It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<ImpersonationSession>()))
            .ReturnsAsync("jwt");

        var cache = new Mock<ICacheService>();
        var db = _h.NewContext();
        var sut = new PlatformImpersonationService(
            db, users.Object, memberships.Object, jwt.Object, cache.Object, _h.Time, NullLogger<PlatformImpersonationService>.Instance);
        return new Rig(sut, db, cache, jwt, target, op);
    }

    private StartImpersonationRequest Request(Rig rig, string reason = "Ticket 1234") =>
        new() { TargetUserId = rig.Target.Id, TenantId = _tenantId, Reason = reason };

    [Fact]
    public async Task Start_RecordsTheSession_HashesTheCode_AndAuditsIt()
    {
        var rig = await CreateRigAsync(PlatformRoles.Support, "Employee");

        var result = await rig.Sut.StartAsync(rig.Operator.Id, Request(rig), Ct);

        var session = await rig.Db.ImpersonationSessions.SingleAsync();
        session.Id.Should().Be(result.SessionId);
        session.ExchangeCodeHash.Should().Be(PlatformImpersonationService.HashCode(result.ExchangeCode)).And.NotBe(result.ExchangeCode);
        session.OperatorId.Should().Be(rig.Operator.Id);
        session.TargetUserId.Should().Be(rig.Target.Id);
        session.Reason.Should().Be("Ticket 1234");
        session.ExpiresAtUtc.Should().Be(_h.Time.GetUtcNow().UtcDateTime.AddMinutes(30));
        (await rig.Db.PlatformAdminAudits.SingleAsync()).Action.Should().Be(ImpersonationActions.Started);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_RequiresAReason(string reason)
    {
        var rig = await CreateRigAsync(PlatformRoles.Support, "Employee");

        var act = () => rig.Sut.StartAsync(rig.Operator.Id, Request(rig, reason), Ct);

        await act.Should().ThrowAsync<GuardException>();
        rig.Db.ImpersonationSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_RefusesARoleWithoutThePermission()
    {
        var rig = await CreateRigAsync(PlatformRoles.Billing, "Employee");

        var act = () => rig.Sut.StartAsync(rig.Operator.Id, Request(rig), Ct);

        await act.Should().ThrowAsync<GuardException>();
    }

    [Fact]
    public async Task Start_SupportCannotImpersonateAnOwner()
    {
        var support = await CreateRigAsync(PlatformRoles.Support, "Owner");

        var act = () => support.Sut.StartAsync(support.Operator.Id, Request(support), Ct);

        await act.Should().ThrowAsync<GuardException>();
        support.Db.ImpersonationSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_SuperAdminCanImpersonateAnOwner()
    {
        var rig = await CreateRigAsync(PlatformRoles.SuperAdmin, "Owner");
        var result = await rig.Sut.StartAsync(rig.Operator.Id, Request(rig), Ct);
        result.ExchangeCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Exchange_ReturnsTheSession_OnceOnly()
    {
        var rig = await CreateRigAsync(PlatformRoles.Support, "Employee");
        var started = await rig.Sut.StartAsync(rig.Operator.Id, Request(rig), Ct);

        var session = await rig.Sut.ExchangeAsync(started.ExchangeCode, Ct);

        session.AccessToken.Should().Be("jwt");
        session.Email.Should().Be("ann@acme.test");
        session.OperatorEmail.Should().Be("ops@platform.test");
        session.Permissions.Should().Contain("Leave:View");

        var again = () => rig.Sut.ExchangeAsync(started.ExchangeCode, Ct);
        await again.Should().ThrowAsync<GuardException>();
    }

    [Fact]
    public async Task Exchange_RefusesAnExpiredCode_AndAWrongOne()
    {
        var rig = await CreateRigAsync(PlatformRoles.Support, "Employee");
        var started = await rig.Sut.StartAsync(rig.Operator.Id, Request(rig), Ct);

        var wrong = () => rig.Sut.ExchangeAsync("not-a-code", Ct);
        await wrong.Should().ThrowAsync<GuardException>();

        _h.Time.Advance(PlatformImpersonationService.CodeLifetime + TimeSpan.FromSeconds(1));
        var expired = () => rig.Sut.ExchangeAsync(started.ExchangeCode, Ct);
        await expired.Should().ThrowAsync<GuardException>();
    }

    [Fact]
    public async Task Stop_EndsTheSession_FlagsItForEveryApi_AndIsIdempotent()
    {
        var rig = await CreateRigAsync(PlatformRoles.Support, "Employee");
        var started = await rig.Sut.StartAsync(rig.Operator.Id, Request(rig), Ct);
        await rig.Sut.ExchangeAsync(started.ExchangeCode, Ct);

        await rig.Sut.StopAsync(started.SessionId, Ct);
        await rig.Sut.StopAsync(started.SessionId, Ct);

        (await rig.Db.ImpersonationSessions.SingleAsync()).EndedAtUtc.Should().NotBeNull();
        rig.Cache.Verify(c => c.SetAsync(ImpersonationClaims.EndedCacheKey(started.SessionId), true, It.IsAny<TimeSpan>()), Times.Once);
        rig.Db.PlatformAdminAudits.Count(a => a.Action == ImpersonationActions.Stopped).Should().Be(1);
    }

    [Fact]
    public async Task ASuperAdminSession_StartsAndStops_WithoutOperatorAuditRows()
    {
        var rig = await CreateRigAsync(PlatformRoles.SuperAdmin, "Owner");

        var started = await rig.Sut.StartAsync(rig.Operator.Id, Request(rig), Ct);
        await rig.Sut.ExchangeAsync(started.ExchangeCode, Ct);
        await rig.Sut.StopAsync(started.SessionId, Ct);

        rig.Db.PlatformAdminAudits.Should().BeEmpty();
        // The session record itself stays: it drives expiry and the stop guard.
        (await rig.Db.ImpersonationSessions.SingleAsync()).EndedAtUtc.Should().NotBeNull();
        rig.Cache.Verify(c => c.SetAsync(ImpersonationClaims.EndedCacheKey(started.SessionId), true, It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task Exchange_RefusesASessionThatWasStopped()
    {
        var rig = await CreateRigAsync(PlatformRoles.Support, "Employee");
        var started = await rig.Sut.StartAsync(rig.Operator.Id, Request(rig), Ct);
        await rig.Sut.StopAsync(started.SessionId, Ct);

        var act = () => rig.Sut.ExchangeAsync(started.ExchangeCode, Ct);

        await act.Should().ThrowAsync<GuardException>();
    }
}
