using OnePunch.Auth.Core.Auditing;
using Onepunch.AuditEngine;
using FluentAssertions;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Onepunch.Auth.Api.Tests.TestSupport;
using Onepunch.Auth.Core.Services;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
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
/// Renaming a company (from HRIS Company Info, via Tenant Service's TenantRenamed) must reach
/// every copy Auth Service keeps: User.DefaultTenantName (what access tokens' tenantName claim is
/// minted from), pending invitations, the tenant creation request -- and every member's session
/// must be told to refresh (membership cache evicted + TenantRenamed push) so the new name shows
/// up without re-logging in.
/// </summary>
public class TenantRenamedWorkerTests
{
    private static (TenantRenamedWorker Worker, AuthContext Context, Mock<ITenantNotificationClient> Client, Mock<ICacheService> Cache) CreateSut()
    {
        var context = AuthTestContextFactory.CreateContext();
        var uow = AuthTestContextFactory.CreateUnitOfWork(context);
        var cache = new Mock<ICacheService>();
        var membershipCacheService = new MembershipCacheService(cache.Object, null!, null!);
        var jwtService = new JwtService(null!, null!, Options.Create(new JwtSettings
        {
            Issuer = "onepunch-auth",
            Audience = ["onepunch-clients"],
        }), null!, null!);
        var userService = new UserService(
            uow, membershipCacheService, null!, null!, null!, IdentityMocks.MockUserManager().Object, null!, null!,
            Mock.Of<IPasswordHasher<User>>(), jwtService, null!, null!, null!, null!, null!, null!,
            null!, Mock.Of<IMapper>(), new AuthAuditRecorder(Mock.Of<IAuditWriter>()));

        var client = new Mock<ITenantNotificationClient>();
        var hubClients = new Mock<IHubClients<ITenantNotificationClient>>();
        hubClients.Setup(c => c.User(It.IsAny<string>())).Returns(client.Object);
        var hubContext = new Mock<IHubContext<TenantHub, ITenantNotificationClient>>();
        hubContext.Setup(h => h.Clients).Returns(hubClients.Object);

        var worker = new TenantRenamedWorker(userService, membershipCacheService, new TenantNotificationService(hubContext.Object));
        return (worker, context, client, cache);
    }

    private static MassTransit.ConsumeContext<TenantRenamed> ContextFor(TenantRenamed message)
    {
        var context = new Mock<MassTransit.ConsumeContext<TenantRenamed>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    [Fact]
    public async Task Consume_UpdatesEveryStoredCopyOfTheName_AndOnlyForThisTenant()
    {
        var (worker, db, _, _) = CreateSut();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var member = new User { Id = Guid.NewGuid(), Email = "a@x.com", DefaultTenantId = tenantId, DefaultTenantName = "Old Co" };
        var outsider = new User { Id = Guid.NewGuid(), Email = "b@x.com", DefaultTenantId = otherTenantId, DefaultTenantName = "Other Co" };
        db.Users.AddRange(member, outsider);
        db.Invitations.AddRange(
            new Invitation { Id = Guid.NewGuid(), TenantId = tenantId, TenantName = "Old Co", Email = "p@x.com", Token = "t1", Status = InvitationStatus.Pending },
            new Invitation { Id = Guid.NewGuid(), TenantId = tenantId, TenantName = "Old Co", Email = "q@x.com", Token = "t2", Status = InvitationStatus.Accepted });
        db.TenantCreationRequests.Add(new TenantCreationRequestStatus { Id = Guid.NewGuid(), TenantId = tenantId, TenantName = "Old Co" });
        await db.SaveChangesAsync();

        await worker.Consume(ContextFor(new TenantRenamed { TenantId = tenantId, TenantName = "New Co", MemberUserIds = [member.Id] }));

        (await db.Users.SingleAsync(x => x.Id == member.Id)).DefaultTenantName.Should().Be("New Co");
        (await db.Users.SingleAsync(x => x.Id == outsider.Id)).DefaultTenantName.Should().Be("Other Co");
        (await db.Invitations.SingleAsync(x => x.Token == "t1")).TenantName.Should().Be("New Co");
        (await db.Invitations.SingleAsync(x => x.Token == "t2")).TenantName.Should().Be("Old Co", "an accepted invitation is history");
        (await db.TenantCreationRequests.SingleAsync(x => x.TenantId == tenantId)).TenantName.Should().Be("New Co");
    }

    [Fact]
    public async Task Consume_RefreshesEveryMembersSession()
    {
        var (worker, _, client, cache) = CreateSut();
        var tenantId = Guid.NewGuid();
        var members = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        await worker.Consume(ContextFor(new TenantRenamed { TenantId = tenantId, TenantName = "New Co", MemberUserIds = members }));

        foreach (var userId in members)
            cache.Verify(c => c.RemoveAsync($"membership:{userId}"), Times.Once);
        client.Verify(c => c.TenantRenamed(It.Is<TenantRenamedNotification>(n => n.TenantId == tenantId && n.TenantName == "New Co")), Times.Exactly(2));
        client.Verify(c => c.RolesChanged(It.IsAny<RolesChangedNotification>()), Times.Never);
    }

    [Fact]
    public async Task Consume_BlankName_IsIgnored()
    {
        var (worker, _, client, _) = CreateSut();

        await worker.Consume(ContextFor(new TenantRenamed { TenantId = Guid.NewGuid(), TenantName = "  ", MemberUserIds = [Guid.NewGuid()] }));

        client.Verify(c => c.TenantRenamed(It.IsAny<TenantRenamedNotification>()), Times.Never);
    }
}
