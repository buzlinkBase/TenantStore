using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

public class PermissionCatalogSeederServiceTests
{
    private static PermissionCatalogSeederService CreateSut(out IUnitOfWorkService uow)
    {
        uow = TenantTestContextFactory.CreateUnitOfWork();
        return new PermissionCatalogSeederService(uow);
    }

    [Fact]
    public async Task EnsureSeededAsync_SeedsPermissionsAndTheFourSystemRoles()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var permissionCount = await uow.Context.Permissions.CountAsync();
        permissionCount.Should().BeGreaterThan(0);

        var systemRoles = await uow.Context.Roles.Where(x => x.IsSystemRole).ToListAsync();
        systemRoles.Select(x => x.Name).Should().BeEquivalentTo(["Owner", "Admin", "Member", "Employee"]);
    }

    [Fact]
    public async Task EnsureSeededAsync_OwnerGetsEveryPermission()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var totalPermissions = await uow.Context.Permissions.CountAsync();
        var owner = await uow.Context.Roles.FirstAsync(x => x.Name == "Owner");
        var ownerGrantCount = await uow.Context.RolePermissions.CountAsync(x => x.RoleId == owner.Id);

        ownerGrantCount.Should().Be(totalPermissions);
    }

    [Fact]
    public async Task EnsureSeededAsync_MemberGetsNoPermissions()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var member = await uow.Context.Roles.FirstAsync(x => x.Name == "Member");
        var memberGrantCount = await uow.Context.RolePermissions.CountAsync(x => x.RoleId == member.Id);

        memberGrantCount.Should().Be(0);
    }

    [Fact]
    public async Task EnsureSeededAsync_IsIdempotent_DoesNotDuplicateOnSecondRun()
    {
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var permissionCountAfterFirst = await uow.Context.Permissions.CountAsync();
        var roleCountAfterFirst = await uow.Context.Roles.CountAsync();

        await sut.EnsureSeededAsync(CancellationToken.None);

        (await uow.Context.Permissions.CountAsync()).Should().Be(permissionCountAfterFirst);
        (await uow.Context.Roles.CountAsync()).Should().Be(roleCountAfterFirst);
    }

    [Fact]
    public async Task EnsureSeededAsync_BackfillsExistingMembershipRoleRowsByName()
    {
        var sut = CreateSut(out var uow);
        var membership = new UserMembership { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        await uow.Repository.AddAsync(membership, CancellationToken.None);
        var membershipRole = new MembershipRole { UserMembershipId = membership.Id, Role = "Owner" };
        await uow.Repository.AddAsync(membershipRole, CancellationToken.None);
        await uow.CommitChangesAsync("", CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var owner = await uow.Context.Roles.FirstAsync(x => x.Name == "Owner");
        var reloaded = await uow.Context.MembershipRoles.FirstAsync(x => x.Id == membershipRole.Id);
        reloaded.RoleId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task EnsureSeededAsync_DoesNotTouchAlreadyBackfilledRows()
    {
        var sut = CreateSut(out var uow);
        await sut.EnsureSeededAsync(CancellationToken.None);
        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");

        var membership = new UserMembership { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        await uow.Repository.AddAsync(membership, CancellationToken.None);
        // Deliberately mismatched Role string vs RoleId -- proves an already-backfilled row
        // (RoleId already set) is left alone rather than being re-derived from the string.
        var membershipRole = new MembershipRole { UserMembershipId = membership.Id, Role = "Owner", RoleId = admin.Id };
        await uow.Repository.AddAsync(membershipRole, CancellationToken.None);
        // Raw SaveChangesAsync, not uow.CommitChangesAsync -- the seeder call above already
        // consumed this uow's one-shot commit (see EnsureSeededAsync's own final commit); a second
        // CommitChangesAsync on the same instance would silently no-op.
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var reloaded = await uow.Context.MembershipRoles.FirstAsync(x => x.Id == membershipRole.Id);
        reloaded.RoleId.Should().Be(admin.Id);
    }
}
