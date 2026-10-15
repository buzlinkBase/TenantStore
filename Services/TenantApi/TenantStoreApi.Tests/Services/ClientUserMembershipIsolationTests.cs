using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

/// <summary>
/// A membership with Category=ClientUser belongs to the client portal. It must never surface in
/// anything HRIS-facing: the members list, the tenant picker / token claims, the company-management
/// gate, admin seat counts, the rename push, or the agency's role lists.
/// </summary>
public class ClientUserMembershipIsolationTests
{
    private static async Task<(IUnitOfWorkService Uow, UserMembershipService Sut)> CreateSutAsync()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        await new PermissionCatalogSeederService(uow).EnsureSeededAsync(CancellationToken.None);
        var sut = new UserMembershipService(uow, Mock.Of<IPublishEndpoint>(), new RoleService(uow), new MembershipRoleService(uow));
        return (uow, sut);
    }

    private static UserMembership Staff(Guid tenantId, Guid userId) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, TenantName = "Acme" };

    private static UserMembership ClientUser(Guid tenantId, Guid userId) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, TenantName = "Acme", Category = MembershipCategory.ClientUser };

    [Fact]
    public void NewMembership_DefaultsToInternalEmployee()
    {
        new UserMembership().Category.Should().Be(MembershipCategory.InternalEmployee);
    }

    [Fact]
    public async Task GetMembersAsync_ExcludesClientUsers()
    {
        var (uow, sut) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var staffUser = Guid.NewGuid();
        await sut.AddAsync(Staff(tenantId, staffUser), [TenantRoles.Member], CancellationToken.None);
        await sut.AddAsync(ClientUser(tenantId, Guid.NewGuid()), [TenantRoles.Member], CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        var members = await sut.GetMembersAsync(tenantId);

        members.Should().ContainSingle().Which.UserId.Should().Be(staffUser);
    }

    [Fact]
    public async Task GetUserMembersAsync_ExcludesClientUserMemberships_SoNoHrisTenantOrClaimsAreIssued()
    {
        var (uow, sut) = await CreateSutAsync();
        var userId = Guid.NewGuid();
        var staffTenant = Guid.NewGuid();
        var clientTenant = Guid.NewGuid();
        await sut.AddAsync(Staff(staffTenant, userId), [TenantRoles.Member], CancellationToken.None);
        await sut.AddAsync(ClientUser(clientTenant, userId), [TenantRoles.Member], CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        var memberships = await sut.GetUserMembersAsync(userId);

        memberships.Should().ContainSingle().Which.TenantId.Should().Be(staffTenant);
    }

    [Fact]
    public async Task GetMemberAsync_ClientUserOnly_IsNotFound()
    {
        var (uow, sut) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = ClientUser(tenantId, userId);
        await sut.AddAsync(client, [TenantRoles.Admin], CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        (await sut.GetMemberAsync(userId, tenantId)).Should().BeNull();
        (await sut.GetMemberByIdAsync(client.Id, tenantId)).Should().BeNull();
    }

    [Fact]
    public async Task GetMemberAsync_PersonWhoIsBothStaffAndClientUser_ResolvesTheStaffMembership()
    {
        var (uow, sut) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var staff = Staff(tenantId, userId);
        await sut.AddAsync(staff, [TenantRoles.Member], CancellationToken.None);
        await sut.AddAsync(ClientUser(tenantId, userId), [TenantRoles.Admin], CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        var resolved = await sut.GetMemberAsync(userId, tenantId);

        resolved.Should().NotBeNull();
        resolved!.Id.Should().Be(staff.Id);
        resolved.IsClientUser().Should().BeFalse();
    }

    [Fact]
    public async Task CanManageCompanyAsync_ClientUserWithAdminRole_IsFalse()
    {
        var (uow, sut) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await sut.AddAsync(ClientUser(tenantId, userId), [TenantRoles.Admin], CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        (await sut.CanManageCompanyAsync(userId, tenantId)).Should().BeFalse();
    }

    [Fact]
    public async Task AdminSeatCount_DoesNotCountClientUsers()
    {
        var (uow, sut) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        await sut.AddAsync(Staff(tenantId, Guid.NewGuid()), [TenantRoles.Admin], CancellationToken.None);
        await sut.AddAsync(ClientUser(tenantId, Guid.NewGuid()), [TenantRoles.Admin], CancellationToken.None);
        await sut.AddAsync(ClientUser(tenantId, Guid.NewGuid()), [TenantRoles.Admin], CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        var counts = await AdminSeatUsageProvider.CountAsync(uow.Context, [tenantId], CancellationToken.None);

        counts.GetValueOrDefault(tenantId).Should().Be(1);
    }

    [Fact]
    public async Task RenameAsync_RenamesClientUserRows_ButOnlyPushesToStaff()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        var publisher = new Mock<IPublishEndpoint>();
        var sut = new TenantService(uow, null!, null!, publisher.Object, null!);
        var tenantId = Guid.NewGuid();
        var staffUser = Guid.NewGuid();
        var clientUser = Guid.NewGuid();
        uow.Context.Tenants.Add(new Tenant { Id = tenantId, TenantName = "My Space" });
        uow.Context.Memberships.AddRange(Staff(tenantId, staffUser), ClientUser(tenantId, clientUser));
        await uow.Context.SaveChangesAsync();

        await sut.RenameAsync(tenantId, "Acme Corp", CancellationToken.None);
        await uow.Context.SaveChangesAsync();

        (await uow.Context.Memberships.Where(x => x.TenantId == tenantId).Select(x => x.TenantName).ToListAsync())
            .Should().AllBe("Acme Corp");
        publisher.Verify(p => p.Publish(
            It.Is<TenantRenamed>(m => m.MemberUserIds.Count == 1 && m.MemberUserIds.Contains(staffUser)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClientCustomRoles_AreNotListedOrResolvedForTheAgency()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        await new PermissionCatalogSeederService(uow).EnsureSeededAsync(CancellationToken.None);
        var roles = new RoleService(uow);
        var tenantId = Guid.NewGuid();
        uow.Context.Roles.AddRange(
            new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Billing", Description = "agency role" },
            new Role { Id = Guid.NewGuid(), TenantId = tenantId, ClientEntityId = Guid.NewGuid(), Audience = RoleAudience.Client, Name = "Finance", Description = "client role" });
        await uow.Context.SaveChangesAsync();

        var listed = await roles.FindAllForTenantAsync(tenantId, CancellationToken.None);

        listed.Select(r => r.Name).Should().Contain("Billing").And.NotContain("Finance");
        (await roles.FindByNameAsync("Finance", tenantId, CancellationToken.None)).Should().BeNull();
        (await roles.IsAssignableAsync("Finance", tenantId, CancellationToken.None)).Should().BeFalse();
        (await roles.FindByNameAsync("Billing", tenantId, CancellationToken.None)).Should().NotBeNull();
    }
}
