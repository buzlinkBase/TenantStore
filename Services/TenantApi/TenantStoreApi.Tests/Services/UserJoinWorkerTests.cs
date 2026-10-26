using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Messaging;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

/// <summary>
/// UserJoin arrives after Auth's synchronous activation, so it may be late: an admin can revoke
/// or remove the member in between, and the event must not undo that.
/// </summary>
public class UserJoinWorkerTests
{
    private static async Task<(UserJoinWorker Worker, UserMembershipService Members, Mock<IPublishEndpoint> Publisher)> CreateAsync()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        await new PermissionCatalogSeederService(uow).EnsureSeededAsync(CancellationToken.None);
        var publisher = new Mock<IPublishEndpoint>();
        var members = new UserMembershipService(uow, publisher.Object, new RoleService(uow), new MembershipRoleService(uow));
        var tenants = new TenantService(uow, null!, null!, publisher.Object, null!);
        return (new UserJoinWorker(tenants, members, publisher.Object), members, publisher);
    }

    private static Mock<ConsumeContext<UserJoin>> ContextFor(Guid userId, Guid tenantId)
    {
        var context = new Mock<ConsumeContext<UserJoin>>();
        context.SetupGet(c => c.Message).Returns(new UserJoin
        {
            UserId = userId,
            TenantId = tenantId,
            TenantName = "Acme",
            Email = "invitee@test.com",
            FullName = "Invitee",
            Roles = ["Employee"],
        });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    private static async Task<UserMembership> AddMemberAsync(UserMembershipService members, Guid tenantId, Guid userId)
    {
        var membership = new UserMembership { TenantId = tenantId, UserId = userId };
        await members.AddAsync(membership, ["Employee"], CancellationToken.None);
        await members.Context.SaveChangesAsync(CancellationToken.None);
        return membership;
    }

    [Fact]
    public async Task Consume_ForAnActiveMember_AnnouncesTheJoin()
    {
        var (worker, members, publisher) = await CreateAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await AddMemberAsync(members, tenantId, userId);

        await worker.Consume(ContextFor(userId, tenantId).Object);

        publisher.Verify(p => p.Publish(
            It.Is<UserJoinToTenantPayload>(m => m.UserId == userId && m.TenantId == tenantId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_DoesNotReactivateAMemberRevokedSinceTheyAccepted()
    {
        var (worker, members, publisher) = await CreateAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var membership = await AddMemberAsync(members, tenantId, userId);
        membership.Status = "Revoked";
        await members.Context.SaveChangesAsync(CancellationToken.None);

        await worker.Consume(ContextFor(userId, tenantId).Object);

        (await members.GetMemberAsync(userId, tenantId, CancellationToken.None))!.Status.Should().Be("Revoked");
        publisher.Verify(p => p.Publish(It.IsAny<UserJoinToTenantPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_DoesNotRecreateAMemberRemovedSinceTheyAccepted()
    {
        var (worker, members, publisher) = await CreateAsync();
        var tenantId = Guid.NewGuid();

        await worker.Consume(ContextFor(Guid.NewGuid(), tenantId).Object);

        (await members.Context.Memberships.CountAsync(x => x.TenantId == tenantId)).Should().Be(0);
        publisher.Verify(p => p.Publish(It.IsAny<UserJoinToTenantPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
