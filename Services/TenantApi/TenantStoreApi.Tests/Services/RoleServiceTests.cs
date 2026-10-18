using FluentAssertions;
using MassTransit;
using Moq;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.DTO;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

public class RoleServiceTests
{
    private static RoleService CreateSut(out IUnitOfWorkService uow)
    {
        uow = TenantTestContextFactory.CreateUnitOfWork();
        return new RoleService(uow);
    }

    [Fact]
    public async Task AddCustomRoleAsync_CreatesTenantScopedRole()
    {
        var sut = CreateSut(out _);
        var tenantId = Guid.NewGuid();

        var role = await sut.AddCustomRoleAsync(tenantId, "Payroll Approver", "Approves payroll runs", CancellationToken.None);

        role.TenantId.Should().Be(tenantId);
        role.IsSystemRole.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateCustomRoleAsync_ThrowsInvalidOperation_WhenRoleIsSystemRole()
    {
        var sut = CreateSut(out var uow);
        var systemRole = new Role { Name = "Owner", IsSystemRole = true };
        await uow.Repository.AddAsync(systemRole, CancellationToken.None);
        await uow.CommitChangesAsync("", CancellationToken.None);

        var act = () => sut.UpdateCustomRoleAsync(systemRole.Id, "New Name", "New Desc", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteCustomRoleAsync_ThrowsInvalidOperation_WhenRoleIsSystemRole()
    {
        var sut = CreateSut(out var uow);
        var systemRole = new Role { Name = "Admin", IsSystemRole = true };
        await uow.Repository.AddAsync(systemRole, CancellationToken.None);
        await uow.CommitChangesAsync("", CancellationToken.None);

        var act = () => sut.DeleteCustomRoleAsync(systemRole.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteCustomRoleAsync_ThrowsInvalidOperation_WhenStillAssignedToAMember()
    {
        var sut = CreateSut(out var uow);
        var tenantId = Guid.NewGuid();
        var customRole = await sut.AddCustomRoleAsync(tenantId, "Custom", "", CancellationToken.None);

        var membership = new UserMembership { TenantId = tenantId, UserId = Guid.NewGuid() };
        await uow.Repository.AddAsync(membership, CancellationToken.None);
        await uow.Repository.AddAsync(new MembershipRole { UserMembershipId = membership.Id, Role = "Custom", RoleId = customRole.Id }, CancellationToken.None);
        // Raw SaveChangesAsync, not uow.CommitChangesAsync -- AddCustomRoleAsync above already
        // consumed this uow's one-shot commit; a second CommitChangesAsync call on the same
        // instance would silently no-op and leave this setup data unpersisted.
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        var act = () => sut.DeleteCustomRoleAsync(customRole.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetPermissionsAsync_ThrowsInvalidOperation_WhenRoleIsSystemRole()
    {
        var sut = CreateSut(out var uow);
        var systemRole = new Role { Name = "None", IsSystemRole = true };
        await uow.Repository.AddAsync(systemRole, CancellationToken.None);
        await uow.CommitChangesAsync("", CancellationToken.None);

        var act = () => sut.SetPermissionsAsync(systemRole.Id, [Guid.NewGuid()], CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static async Task<(Role Role, Permission View, Permission Edit)> RoleWithTwoPermissionsAsync(RoleService sut, IUnitOfWorkService uow)
    {
        var role = await sut.AddCustomRoleAsync(Guid.NewGuid(), "Custom", "", CancellationToken.None);
        var view = new Permission { Module = "Security", Feature = "Users", Action = "View", Code = "Users:View" };
        var edit = new Permission { Module = "Security", Feature = "Users", Action = "Edit", Code = "Users:Edit" };
        uow.Context.Permissions.AddRange(view, edit);
        await uow.Context.SaveChangesAsync(CancellationToken.None);
        await sut.SetPermissionsAsync(role.Id, [view.Id, edit.Id], CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);
        return (role, view, edit);
    }

    [Fact]
    public async Task SetPermissionsAsync_HardDeletesARevokedPermission_LeavingNoRowBehind()
    {
        var sut = CreateSut(out var uow);
        var (role, view, edit) = await RoleWithTwoPermissionsAsync(sut, uow);

        await sut.SetPermissionsAsync(role.Id, [view.Id], CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        var rows = await uow.Context.RolePermissions.IgnoreQueryFilters().Where(x => x.RoleId == role.Id).ToListAsync();
        rows.Should().ContainSingle().Which.PermissionId.Should().Be(view.Id);
        rows.Should().NotContain(x => x.PermissionId == edit.Id, "a revoked grant is deleted, not soft-deleted");
    }

    [Fact]
    public async Task SetPermissionsAsync_RemovingEverything_LeavesTheRoleWithNoRows()
    {
        var sut = CreateSut(out var uow);
        var (role, _, _) = await RoleWithTwoPermissionsAsync(sut, uow);

        await sut.SetPermissionsAsync(role.Id, [], CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        (await uow.Context.RolePermissions.IgnoreQueryFilters().CountAsync(x => x.RoleId == role.Id)).Should().Be(0);
    }

    [Fact]
    public async Task SetPermissionsAsync_ReGrantingARevokedPermission_Works()
    {
        var sut = CreateSut(out var uow);
        var (role, view, edit) = await RoleWithTwoPermissionsAsync(sut, uow);
        await sut.SetPermissionsAsync(role.Id, [view.Id], CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.SetPermissionsAsync(role.Id, [view.Id, edit.Id], CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        var rows = await uow.Context.RolePermissions.Where(x => x.RoleId == role.Id).ToListAsync();
        rows.Select(x => x.PermissionId).Should().BeEquivalentTo([view.Id, edit.Id]);
    }

    [Fact]
    public async Task SetPermissionsAsync_ClearsRowsSoftDeletedBeforeTheChange()
    {
        var sut = CreateSut(out var uow);
        var (role, view, edit) = await RoleWithTwoPermissionsAsync(sut, uow);
        // What older versions left behind for a revoked grant.
        var legacy = await uow.Context.RolePermissions.SingleAsync(x => x.RoleId == role.Id && x.PermissionId == edit.Id);
        legacy.DeletedAt = DateTime.UtcNow;
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.SetPermissionsAsync(role.Id, [view.Id], CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        (await uow.Context.RolePermissions.IgnoreQueryFilters().AnyAsync(x => x.PermissionId == edit.Id && x.RoleId == role.Id))
            .Should().BeFalse("the leftover soft-deleted row is purged when it isn't granted");
    }

    [Fact]
    public async Task SetPermissionsAsync_ReGrantingALegacySoftDeletedRow_ReactivatesIt()
    {
        var sut = CreateSut(out var uow);
        var (role, view, edit) = await RoleWithTwoPermissionsAsync(sut, uow);
        var legacy = await uow.Context.RolePermissions.SingleAsync(x => x.RoleId == role.Id && x.PermissionId == edit.Id);
        legacy.DeletedAt = DateTime.UtcNow;
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.SetPermissionsAsync(role.Id, [view.Id, edit.Id], CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        (await uow.Context.RolePermissions.Where(x => x.RoleId == role.Id).Select(x => x.PermissionId).ToListAsync())
            .Should().BeEquivalentTo([view.Id, edit.Id]);
    }

    [Fact]
    public async Task SetPermissionsAsync_ReplacesExistingSetOnACustomRole()
    {
        var sut = CreateSut(out var uow);
        var tenantId = Guid.NewGuid();
        var role = await sut.AddCustomRoleAsync(tenantId, "Custom", "", CancellationToken.None);
        var permission = new Permission { Module = "Payroll", Feature = "Payroll Run", Action = "Approve", Code = "Payroll Run:Approve" };
        await uow.Repository.AddAsync(permission, CancellationToken.None);
        // Raw SaveChangesAsync -- AddCustomRoleAsync above already consumed this uow's one-shot
        // commit. Then a genuinely fresh IUnitOfWorkService (same underlying Context/database, the
        // way a real second HTTP request would get its own scoped instance) for SetPermissionsAsync,
        // so its own CommitChangesAsync call is a real first commit rather than a silent no-op.
        await uow.Context.SaveChangesAsync(CancellationToken.None);
        var sutWithFreshUow = new RoleService(TenantTestContextFactory.CreateUnitOfWork(uow.Context));

        await sutWithFreshUow.SetPermissionsAsync(role.Id, [permission.Id], CancellationToken.None);

        var reloaded = await sut.FindOneWithPermissionsAsync(role.Id, CancellationToken.None);
        reloaded!.RolePermissions.Should().ContainSingle(rp => rp.PermissionId == permission.Id);
    }

    // Regression test for a production DbUpdateException: revoking then re-granting the same
    // permission (e.g. an admin unchecks then re-checks a box, or two saves toggle it back and
    // forth) used to insert a brand-new RolePermission row for a permission that still had a
    // soft-deleted one from the earlier revoke, colliding on
    // IX_RolePermissions_RoleId_PermissionId. SetPermissionsAsync must reactivate the existing
    // row instead of inserting a duplicate.
    [Fact]
    public async Task SetPermissionsAsync_ReactivatesARevokedPermissionInsteadOfDuplicating()
    {
        var sut = CreateSut(out var uow);
        var tenantId = Guid.NewGuid();
        var role = await sut.AddCustomRoleAsync(tenantId, "Custom", "", CancellationToken.None);
        var permission = new Permission { Module = "Payroll", Feature = "Payroll Run", Action = "Approve", Code = "Payroll Run:Approve" };
        await uow.Repository.AddAsync(permission, CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);
        var context = uow.Context;

        // Grant, then revoke (soft-deletes the row via SetPermissionsAsync's own removal path),
        // then grant again -- this third call is what used to throw.
        await new RoleService(TenantTestContextFactory.CreateUnitOfWork(context))
            .SetPermissionsAsync(role.Id, [permission.Id], CancellationToken.None);
        await new RoleService(TenantTestContextFactory.CreateUnitOfWork(context))
            .SetPermissionsAsync(role.Id, [], CancellationToken.None);

        var act = () => new RoleService(TenantTestContextFactory.CreateUnitOfWork(context))
            .SetPermissionsAsync(role.Id, [permission.Id], CancellationToken.None);

        await act.Should().NotThrowAsync();
        var reloaded = await sut.FindOneWithPermissionsAsync(role.Id, CancellationToken.None);
        reloaded!.RolePermissions.Should().ContainSingle(rp => rp.PermissionId == permission.Id);
    }

    [Fact]
    public async Task IsAssignableAsync_RejectsOwner()
    {
        var sut = CreateSut(out var uow);
        var tenantId = Guid.NewGuid();
        await uow.Repository.AddAsync(new Role { Name = "Owner", IsSystemRole = true }, CancellationToken.None);
        await uow.CommitChangesAsync("", CancellationToken.None);

        var result = await sut.IsAssignableAsync("Owner", tenantId, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAssignableAsync_AcceptsNonOwnerSystemRole()
    {
        var sut = CreateSut(out var uow);
        var tenantId = Guid.NewGuid();
        await uow.Repository.AddAsync(new Role { Name = "Admin", IsSystemRole = true }, CancellationToken.None);
        await uow.CommitChangesAsync("", CancellationToken.None);

        var result = await sut.IsAssignableAsync("Admin", tenantId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAssignableAsync_AcceptsThisTenantsCustomRole_RejectsAnotherTenants()
    {
        var sut = CreateSut(out _);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        await sut.AddCustomRoleAsync(tenantId, "Payroll Approver", "", CancellationToken.None);

        (await sut.IsAssignableAsync("Payroll Approver", tenantId, CancellationToken.None)).Should().BeTrue();
        (await sut.IsAssignableAsync("Payroll Approver", otherTenantId, CancellationToken.None)).Should().BeFalse();
    }

    // ── Members of an edited role get their sessions refreshed ─────────────

    private sealed record Seeded(Role Role, Permission Permission, Guid ActiveUser, Guid InactiveUser, Guid OtherUser);

    // A custom role held by an active member and a revoked one; a third member has another role.
    private static async Task<Seeded> SeedRoleWithMembersAsync(IUnitOfWorkService uow, Guid tenantId, string roleName = "Custom")
    {
        var role = new Role { Id = Guid.NewGuid(), TenantId = tenantId, Name = roleName, IsSystemRole = false };
        var permission = new Permission { Module = "Payroll", Feature = "Payroll Run", Action = "Approve", Code = "Payroll Run:Approve" };
        var active = new UserMembership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(), Status = "Active" };
        var revoked = new UserMembership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(), Status = "Revoked" };
        var other = new UserMembership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(), Status = "Active" };
        uow.Context.Roles.Add(role);
        uow.Context.Permissions.Add(permission);
        uow.Context.Memberships.AddRange(active, revoked, other);
        uow.Context.MembershipRoles.AddRange(
            new MembershipRole { UserMembershipId = active.Id, Role = roleName, RoleId = role.Id },
            new MembershipRole { UserMembershipId = active.Id, Role = "Employee" },
            new MembershipRole { UserMembershipId = revoked.Id, Role = roleName, RoleId = role.Id },
            new MembershipRole { UserMembershipId = other.Id, Role = "Admin" });
        await uow.Context.SaveChangesAsync(CancellationToken.None);
        return new Seeded(role, permission, active.UserId, revoked.UserId, other.UserId);
    }

    private static (RoleService Sut, List<MembershipChanged> Published) SutWithPublisher(IUnitOfWorkService seededUow)
    {
        var published = new List<MembershipChanged>();
        var publisher = new Mock<IPublishEndpoint>();
        publisher.Setup(x => x.Publish(It.IsAny<MembershipChanged>(), It.IsAny<CancellationToken>()))
            .Callback<MembershipChanged, CancellationToken>((m, _) => published.Add(m))
            .Returns(Task.CompletedTask);
        return (new RoleService(TenantTestContextFactory.CreateUnitOfWork(seededUow.Context), publisher.Object), published);
    }

    [Fact]
    public async Task SetPermissionsAsync_RefreshesEveryActiveMemberOfTheRole()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        var tenantId = Guid.NewGuid();
        var seeded = await SeedRoleWithMembersAsync(uow, tenantId);
        var (sut, published) = SutWithPublisher(uow);

        await sut.SetPermissionsAsync(seeded.Role.Id, [seeded.Permission.Id], CancellationToken.None);

        published.Should().ContainSingle();
        published[0].UserId.Should().Be(seeded.ActiveUser);
        published[0].TenantId.Should().Be(tenantId);
        published[0].ChangeType.Should().Be("RoleChanged");
        published[0].NewRoles.Should().BeEquivalentTo(["Custom", "Employee"]);
    }

    [Fact]
    public async Task UpdateCustomRoleAsync_Rename_UpdatesMembersRoleNames_AndRefreshesThem()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        var tenantId = Guid.NewGuid();
        var seeded = await SeedRoleWithMembersAsync(uow, tenantId, "Payroll Clerk");
        var (sut, published) = SutWithPublisher(uow);

        await sut.UpdateCustomRoleAsync(seeded.Role.Id, "Payroll Officer", "", CancellationToken.None);

        published.Should().ContainSingle(m => m.UserId == seeded.ActiveUser)
            .Which.NewRoles.Should().BeEquivalentTo(["Payroll Officer", "Employee"]);
        (await uow.Context.MembershipRoles.Where(x => x.RoleId == seeded.Role.Id).Select(x => x.Role).ToListAsync())
            .Should().OnlyContain(name => name == "Payroll Officer");
    }

    [Fact]
    public async Task UpdateCustomRoleAsync_DescriptionOnly_RefreshesNobody()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        var seeded = await SeedRoleWithMembersAsync(uow, Guid.NewGuid());
        var (sut, published) = SutWithPublisher(uow);

        await sut.UpdateCustomRoleAsync(seeded.Role.Id, "Custom", "New description", CancellationToken.None);

        published.Should().BeEmpty();
    }
}
