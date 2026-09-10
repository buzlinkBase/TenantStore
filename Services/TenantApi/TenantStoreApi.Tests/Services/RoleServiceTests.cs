using FluentAssertions;
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
        await uow.CommitChangesAsync("", CancellationToken.None);

        var act = () => sut.DeleteCustomRoleAsync(customRole.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetPermissionsAsync_ThrowsInvalidOperation_WhenRoleIsSystemRole()
    {
        var sut = CreateSut(out var uow);
        var systemRole = new Role { Name = "Member", IsSystemRole = true };
        await uow.Repository.AddAsync(systemRole, CancellationToken.None);
        await uow.CommitChangesAsync("", CancellationToken.None);

        var act = () => sut.SetPermissionsAsync(systemRole.Id, [Guid.NewGuid()], CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetPermissionsAsync_ReplacesExistingSetOnACustomRole()
    {
        var sut = CreateSut(out var uow);
        var tenantId = Guid.NewGuid();
        var role = await sut.AddCustomRoleAsync(tenantId, "Custom", "", CancellationToken.None);
        var permission = new Permission { Module = "Payroll", Feature = "Payroll Run", Action = "Approve", Code = "Payroll Run:Approve" };
        await uow.Repository.AddAsync(permission, CancellationToken.None);
        await uow.CommitChangesAsync("", CancellationToken.None);

        await sut.SetPermissionsAsync(role.Id, [permission.Id], CancellationToken.None);

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
}
