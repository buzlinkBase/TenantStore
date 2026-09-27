using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

/// <summary>
/// Renaming a company from HRIS Company Info: every copy of the name Tenant Service holds (the
/// Tenant row, each member's UserMembership -- what the company switcher lists -- and delegations
/// naming it as guest) must change, and Auth Service must be told (TenantRenamed) so it can update
/// its own copies and refresh members' sessions.
/// </summary>
public class TenantServiceRenameTests
{
    private static (TenantService Sut, IUnitOfWorkService Uow, Mock<IPublishEndpoint> Publisher) CreateSut()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        var publisher = new Mock<IPublishEndpoint>();
        return (new TenantService(uow, null!, null!, publisher.Object, null!), uow, publisher);
    }

    [Fact]
    public async Task RenameAsync_UpdatesEveryCopy_AndTellsAuthServiceWhoToRefresh()
    {
        var (sut, uow, publisher) = CreateSut();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        uow.Context.Tenants.Add(new Tenant { Id = tenantId, TenantName = "My Space" });
        uow.Context.Memberships.AddRange(
            new UserMembership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = memberA, TenantName = "My Space" },
            new UserMembership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = memberB, TenantName = "My Space" },
            // Invite placeholder -- no account yet, so nobody to refresh.
            new UserMembership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.Empty, TenantName = "My Space", InvitedEmail = "p@x.com" },
            new UserMembership { Id = Guid.NewGuid(), TenantId = otherTenantId, UserId = memberA, TenantName = "Elsewhere" });
        uow.Context.TenantDelegations.Add(new TenantDelegation { Id = Guid.NewGuid(), HostTenantId = otherTenantId, GuestTenantId = tenantId, GuestTenantName = "My Space" });
        await uow.Context.SaveChangesAsync();

        await sut.RenameAsync(tenantId, "  Acme Corp  ", CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        (await uow.Context.Tenants.SingleAsync(x => x.Id == tenantId)).TenantName.Should().Be("Acme Corp");
        (await uow.Context.Memberships.Where(x => x.TenantId == tenantId).Select(x => x.TenantName).ToListAsync())
            .Should().AllBe("Acme Corp");
        (await uow.Context.Memberships.SingleAsync(x => x.TenantId == otherTenantId)).TenantName.Should().Be("Elsewhere");
        (await uow.Context.TenantDelegations.SingleAsync()).GuestTenantName.Should().Be("Acme Corp");

        publisher.Verify(p => p.Publish(
            It.Is<TenantRenamed>(m => m.TenantId == tenantId
                && m.TenantName == "Acme Corp"
                && m.MemberUserIds.Count == 2
                && m.MemberUserIds.Contains(memberA)
                && m.MemberUserIds.Contains(memberB)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameAsync_BlankName_IsRejected()
    {
        var (sut, uow, publisher) = CreateSut();
        var tenantId = Guid.NewGuid();
        uow.Context.Tenants.Add(new Tenant { Id = tenantId, TenantName = "My Space" });
        await uow.Context.SaveChangesAsync();

        var act = () => sut.RenameAsync(tenantId, "   ", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        publisher.Verify(p => p.Publish(It.IsAny<TenantRenamed>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenameAsync_UnknownTenant_IsNotFound()
    {
        var (sut, _, _) = CreateSut();

        var act = () => sut.RenameAsync(Guid.NewGuid(), "Acme", CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Endpoint gate (UserMembershipService.CanManageCompanyAsync) ──────────────────────────
    // HRIS forwards the caller's own token, so the rename endpoint itself must refuse
    // portal-only members (Employee/Client) -- they never get onto Company Settings anyway.

    private static async Task<bool> CanManageAsync(string status, params string[] roles)
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        await new PermissionCatalogSeederService(uow).EnsureSeededAsync(CancellationToken.None);
        var membershipService = new UserMembershipService(uow, Mock.Of<IPublishEndpoint>(), new RoleService(uow), new MembershipRoleService(uow));
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await membershipService.AddAsync(new UserMembership { TenantId = tenantId, UserId = userId, Status = status }, roles, CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        return await membershipService.CanManageCompanyAsync(userId, tenantId, CancellationToken.None);
    }

    [Theory]
    [InlineData("Active", new[] { "Admin" }, true)]
    [InlineData("Active", new[] { "Member" }, true)]
    [InlineData("Active", new[] { "Employee", "Member" }, true)]
    [InlineData("Active", new[] { "Employee" }, false)]
    [InlineData("Active", new[] { "Client" }, false)]
    [InlineData("Revoked", new[] { "Admin" }, false)]
    public async Task CanManageCompanyAsync_OnlyActiveNonPortalMembers(string status, string[] roles, bool expected)
    {
        (await CanManageAsync(status, roles)).Should().Be(expected);
    }

    [Fact]
    public async Task CanManageCompanyAsync_NonMember_IsFalse()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        var membershipService = new UserMembershipService(uow, Mock.Of<IPublishEndpoint>(), new RoleService(uow), new MembershipRoleService(uow));

        (await membershipService.CanManageCompanyAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None)).Should().BeFalse();
    }
}
