using FluentAssertions;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Services;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Cache;
using Onepunch.Common.Lib.DTO;
using OnePunch.Auth.Core.Hubs;
using OnePunch.Auth.Core.Messaging;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Messaging;

/// <summary>
/// Covers the two pushes added to MembershipChangedWorker: a "RolesChanged" SignalR notification
/// for role edits, and a "SessionRevoked" forced-logout push (now carrying the affected TenantId)
/// when a member's status changes to anything other than Active (Revoked, Inactive, ...) -- both
/// fired to the affected user's TenantHub connection so a connected frontend reacts immediately
/// instead of waiting for its JWT to expire. Most of these tests don't re-exercise the
/// pre-existing cache-eviction/ApplyMembershipChangeAsync behavior (none of those messages carry
/// an actual role diff, so ApplyMembershipChangeAsync's own early-return keeps the UserService
/// dependency graph untouched, matching how InvitationServiceTests passes `null!` for services
/// unused by the branch under test) -- except the last test, which specifically targets
/// ApplyMembershipChangeAsync directly to guard against the account-status-lockout bug.
/// </summary>
public class MembershipChangedWorkerTests
{
    private static (MembershipChangedWorker Worker, Mock<ITenantNotificationClient> Client) CreateSut()
    {
        var membershipCacheService = new MembershipCacheService(Mock.Of<ICacheService>(), null!, null!);

        var userManager = IdentityMocks.MockUserManager();
        var jwtService = new JwtService(null!, null!, Options.Create(new JwtSettings
        {
            Issuer = "onepunch-auth",
            Audience = ["onepunch-clients"],
        }), null!, null!);
        var uow = AuthTestContextFactory.CreateUnitOfWork();
        var userService = new UserService(
            uow,
            membershipCacheService,
            null!, // MembershipGrpcClient — unused, this message carries no role/status diff
            null!, // IPublishEndpoint
            null!, // IHttpContextAccessor
            userManager.Object,
            null!, // RoleManager<Role>
            null!, // SignInManager<User>
            Mock.Of<IPasswordHasher<User>>(),
            jwtService,
            null!, // ITenantProvider
            null!, // TenantService
            null!, // IConfiguration
            null!, // EmailTokenService
            null!, // InvitationService
            null!, // EmailNotificationService
            null!, // IMfaChallengeService
            Mock.Of<IMapper>());

        var client = new Mock<ITenantNotificationClient>();
        var hubClients = new Mock<IHubClients<ITenantNotificationClient>>();
        hubClients.Setup(c => c.User(It.IsAny<string>())).Returns(client.Object);
        var hubContext = new Mock<IHubContext<TenantHub, ITenantNotificationClient>>();
        hubContext.Setup(h => h.Clients).Returns(hubClients.Object);
        var notificationService = new TenantNotificationService(hubContext.Object);

        var worker = new MembershipChangedWorker(membershipCacheService, userService, notificationService);
        return (worker, client);
    }

    private static Mock<MassTransit.ConsumeContext<MembershipChanged>> CreateContext(MembershipChanged message)
    {
        var context = new Mock<MassTransit.ConsumeContext<MembershipChanged>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    [Fact]
    public async Task Consume_PushesRolesChanged_WhenChangeTypeIsRoleChanged()
    {
        var (worker, client) = CreateSut();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var message = new MembershipChanged
        {
            UserId = userId,
            TenantId = tenantId,
            ChangeType = "RoleChanged",
            NewRoles = [],
        };

        await worker.Consume(CreateContext(message).Object);

        client.Verify(
            c => c.RolesChanged(It.Is<RolesChangedNotification>(n => n.TenantId == tenantId)),
            Times.Once);
    }

    [Fact]
    public async Task Consume_DoesNotPushRolesChanged_WhenStatusChangedToRevoked()
    {
        var (worker, client) = CreateSut();
        var message = new MembershipChanged
        {
            UserId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ChangeType = "StatusChanged",
            NewStatus = "Revoked",
        };

        await worker.Consume(CreateContext(message).Object);

        client.Verify(c => c.RolesChanged(It.IsAny<RolesChangedNotification>()), Times.Never);
    }

    [Fact]
    public async Task Consume_PushesRolesChanged_WhenStatusChangedBackToActive()
    {
        // Reactivating a member (Revoked/Inactive -> Active) must restore their tenant in the
        // frontend's tenant switcher. It reuses the RolesChanged push -- its handler's refresh
        // pulls the account's current tenant list fresh from the server (guaranteed fresh by the
        // cache invalidation above), which naturally includes the just-reactivated tenant again.
        var (worker, client) = CreateSut();
        var tenantId = Guid.NewGuid();
        var message = new MembershipChanged
        {
            UserId = Guid.NewGuid(),
            TenantId = tenantId,
            ChangeType = "StatusChanged",
            NewStatus = "Active",
        };

        await worker.Consume(CreateContext(message).Object);

        client.Verify(
            c => c.RolesChanged(It.Is<RolesChangedNotification>(n => n.TenantId == tenantId)),
            Times.Once);
        client.Verify(c => c.SessionRevoked(It.IsAny<SessionRevokedNotification>()), Times.Never);
    }

    [Fact]
    public async Task Consume_PushesSessionRevoked_WhenStatusChangedToRevoked()
    {
        var (worker, client) = CreateSut();
        var tenantId = Guid.NewGuid();
        var message = new MembershipChanged
        {
            UserId = Guid.NewGuid(),
            TenantId = tenantId,
            ChangeType = "StatusChanged",
            NewStatus = "Revoked",
        };

        await worker.Consume(CreateContext(message).Object);

        client.Verify(
            c => c.SessionRevoked(It.Is<SessionRevokedNotification>(n => n.TenantId == tenantId)),
            Times.Once);
    }

    [Fact]
    public async Task Consume_PushesSessionRevoked_WhenStatusChangedToInactive()
    {
        var (worker, client) = CreateSut();
        var tenantId = Guid.NewGuid();
        var message = new MembershipChanged
        {
            UserId = Guid.NewGuid(),
            TenantId = tenantId,
            ChangeType = "StatusChanged",
            NewStatus = "Inactive",
        };

        await worker.Consume(CreateContext(message).Object);

        client.Verify(
            c => c.SessionRevoked(It.Is<SessionRevokedNotification>(n => n.TenantId == tenantId)),
            Times.Once);
    }

    [Fact]
    public async Task Consume_DoesNotPushSessionRevoked_WhenStatusChangedToActive()
    {
        var (worker, client) = CreateSut();
        var message = new MembershipChanged
        {
            UserId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ChangeType = "StatusChanged",
            NewStatus = "Active",
        };

        await worker.Consume(CreateContext(message).Object);

        client.Verify(c => c.SessionRevoked(It.IsAny<SessionRevokedNotification>()), Times.Never);
    }

    [Fact]
    public async Task Consume_DoesNotPushSessionRevoked_WhenChangeTypeIsRoleChanged()
    {
        var (worker, client) = CreateSut();
        var message = new MembershipChanged
        {
            UserId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ChangeType = "RoleChanged",
            NewRoles = [],
        };

        await worker.Consume(CreateContext(message).Object);

        client.Verify(c => c.SessionRevoked(It.IsAny<SessionRevokedNotification>()), Times.Never);
    }

    [Fact]
    public async Task ApplyMembershipChangeAsync_NeverTouchesAccountStatus_EvenWhenSyncingRoles()
    {
        // Regression guard for the account-lockout bug: a per-tenant status change must never
        // touch AuthApi's own account-wide User.Status (Login/RefreshLogin gate on it regardless
        // of tenant, so writing "Revoked" there locked the account out of every tenant it
        // belonged to, not just the one being revoked). ApplyMembershipChangeAsync no longer
        // takes a status at all -- this exercises its one remaining write path (the
        // DefaultTenantRoles sync, which DOES call UpdateAsync) and confirms Status is still
        // untouched even then, not just when the method no-ops.
        var context = AuthTestContextFactory.CreateContext();
        var uow = AuthTestContextFactory.CreateUnitOfWork(context);
        var userManager = IdentityMocks.MockUserManager();
        var tenantId = Guid.NewGuid();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@onepunch.local",
            Status = "Active",
            DefaultTenantId = tenantId,
            DefaultTenantRoles = ["Member"],
        };
        userManager.Setup(m => m.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        userManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var membershipCacheService = new MembershipCacheService(Mock.Of<ICacheService>(), null!, null!);
        var jwtService = new JwtService(null!, null!, Options.Create(new JwtSettings
        {
            Issuer = "onepunch-auth",
            Audience = ["onepunch-clients"],
        }), null!, null!);
        var userService = new UserService(
            uow, membershipCacheService, null!, null!, null!, userManager.Object, null!, null!,
            Mock.Of<IPasswordHasher<User>>(), jwtService, null!, null!, null!, null!, null!, null!,
            null!, Mock.Of<IMapper>());

        await userService.ApplyMembershipChangeAsync(user.Id, tenantId, newStatus: null, isRoleChange: true, newRoles: ["Admin"], CancellationToken.None);

        userManager.Verify(m => m.UpdateAsync(user), Times.Once);
        user.Status.Should().Be("Active");
    }
}
