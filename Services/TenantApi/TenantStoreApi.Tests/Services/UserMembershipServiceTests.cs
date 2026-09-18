using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

public class UserMembershipServiceTests
{
    private static async Task<(UserMembershipService Sut, IUnitOfWorkService Uow)> CreateSutAsync()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        await new PermissionCatalogSeederService(uow).EnsureSeededAsync(CancellationToken.None);

        var roleService = new RoleService(uow);
        var sut = new UserMembershipService(uow, Mock.Of<IPublishEndpoint>(), roleService);
        return (sut, uow);
    }

    // Commits via Context.SaveChangesAsync directly rather than sut.CommitChangesAsync -- the
    // shared UnitOfWork's CommitChangesAsync only performs a real commit on its first call per
    // instance (an internal one-shot guard); tests reuse one uow across several memberships, so a
    // second/third call through that wrapper would silently no-op and leave the write unsaved.
    private static async Task<UserMembership> AddMemberAsync(UserMembershipService sut, Guid tenantId, Guid userId, params string[] roles)
    {
        var membership = new UserMembership { TenantId = tenantId, UserId = userId };
        await sut.AddAsync(membership, roles, CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);
        return membership;
    }

    [Fact]
    public async Task AddAsync_ResolvesRoleIdForSeededSystemRoleNames()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();

        var membership = await AddMemberAsync(sut, tenantId, Guid.NewGuid(), "Owner");

        membership.Roles.Should().ContainSingle(r => r.Role == "Owner" && r.RoleId != null);
    }

    [Fact]
    public async Task ReplaceRolesAsync_Succeeds_WhenCallerIsOwner()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, ownerId, "Owner");
        await AddMemberAsync(sut, tenantId, targetId, "Member");

        await sut.ReplaceRolesAsync(ownerId, targetId, tenantId, ["Admin"], CancellationToken.None);

        var target = await sut.GetMemberAsync(targetId, tenantId, CancellationToken.None);
        target!.HasRole("Admin").Should().BeTrue();
    }

    [Fact]
    public async Task ReplaceRolesAsync_RemovesRolesNotInTheNewSelection()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, ownerId, "Owner");
        await AddMemberAsync(sut, tenantId, targetId, "Member", "Admin");

        await sut.ReplaceRolesAsync(ownerId, targetId, tenantId, ["Admin"], CancellationToken.None);

        var target = await sut.GetMemberAsync(targetId, tenantId, CancellationToken.None);
        target!.HasRole("Admin").Should().BeTrue();
        target!.HasRole("Member").Should().BeFalse();
        target!.Roles.Should().ContainSingle();
    }

    [Fact]
    public async Task ReplaceRolesAsync_ThrowsUnauthorized_WhenCallerIsPlainMember()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, memberId, "Member");
        await AddMemberAsync(sut, tenantId, targetId, "Member");

        var act = () => sut.ReplaceRolesAsync(memberId, targetId, tenantId, ["Admin"], CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ReplaceRolesAsync_Succeeds_WhenCallerIsAdmin()
    {
        // Confirms the gate now runs through Admin's seeded Tenant Members:Manage permission,
        // not a hardcoded name check, and behaves identically to before.
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, adminId, "Admin");
        await AddMemberAsync(sut, tenantId, targetId, "Member");

        await sut.ReplaceRolesAsync(adminId, targetId, tenantId, ["Admin"], CancellationToken.None);

        var target = await sut.GetMemberAsync(targetId, tenantId, CancellationToken.None);
        target!.HasRole("Admin").Should().BeTrue();
    }

    [Fact]
    public async Task RemoveMemberAsync_ThrowsUnauthorized_WhenCallerLacksMembersManage()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, memberId, "Member");
        await AddMemberAsync(sut, tenantId, targetId, "Member");

        var act = () => sut.RemoveMemberAsync(memberId, targetId, tenantId, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task EffectivePermissionCodes_ReturnsUnion_WhenMembershipHoldsMultipleRoles()
    {
        var (sut, uow) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var roleService = new RoleService(uow);
        var customRole = await roleService.AddCustomRoleAsync(tenantId, "Payroll Approver", "", CancellationToken.None);
        var permission = new Permission { Module = "Payroll", Feature = "Payroll Run", Action = "Approve", Code = "Payroll Run:Approve" };
        await uow.Repository.AddAsync(permission, CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);
        await roleService.SetPermissionsAsync(customRole.Id, [permission.Id], CancellationToken.None);

        var userId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, userId, "Member", "Payroll Approver");

        var membership = await sut.GetMemberAsync(userId, tenantId, CancellationToken.None);

        membership!.EffectivePermissionCodes().Should().Contain("Payroll Run:Approve");
    }

    [Fact]
    public async Task HasPermission_ReturnsFalse_ForMemberWithNoGrantedPermissions()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, userId, "Member");

        var membership = await sut.GetMemberAsync(userId, tenantId, CancellationToken.None);

        membership!.HasPermission("Tenant Members:Manage").Should().BeFalse();
    }

    // Confirms the actual new capability this pass adds: a Custom Role holding only the new
    // "Security" module's Users:* codes (catalog codes are "{Feature}:{Action}" -- the module
    // name isn't part of the code) -- not "Tenant Members:Manage", not Owner/Admin -- can now
    // perform these actions too, without touching anyone's existing access via the old code.
    [Fact]
    public async Task ReplaceRolesAsync_Succeeds_WhenCallerHasOnlySecurityUsersEditPermission()
    {
        var (sut, uow) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var roleService = new RoleService(uow);
        var customRole = await roleService.AddCustomRoleAsync(tenantId, "User Manager", "", CancellationToken.None);
        var permission = await uow.Context.Permissions.SingleAsync(p => p.Code == "Users:Edit");
        await roleService.SetPermissionsAsync(customRole.Id, [permission.Id], CancellationToken.None);
        // CommitChangesAsync only performs a real commit on its first call per uow instance (see
        // AddMemberAsync's own comment above) -- CreateSutAsync's own seeding already consumed
        // it, so SetPermissionsAsync's commit above is a no-op; force the flush explicitly.
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, callerId, "Member", "User Manager");
        await AddMemberAsync(sut, tenantId, targetId, "Member");

        await sut.ReplaceRolesAsync(callerId, targetId, tenantId, ["Admin"], CancellationToken.None);

        var target = await sut.GetMemberAsync(targetId, tenantId, CancellationToken.None);
        target!.HasRole("Admin").Should().BeTrue();
    }

    [Fact]
    public async Task RemoveMemberAsync_Succeeds_WhenCallerHasOnlySecurityUsersDeletePermission()
    {
        var (sut, uow) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var roleService = new RoleService(uow);
        var customRole = await roleService.AddCustomRoleAsync(tenantId, "User Remover", "", CancellationToken.None);
        var permission = await uow.Context.Permissions.SingleAsync(p => p.Code == "Users:Delete");
        await roleService.SetPermissionsAsync(customRole.Id, [permission.Id], CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, callerId, "Member", "User Remover");
        await AddMemberAsync(sut, tenantId, targetId, "Member");

        await sut.RemoveMemberAsync(callerId, targetId, tenantId, CancellationToken.None);
        // RemoveMemberAsync itself only marks the entity removed (soft-delete interceptor) --
        // flushing is the caller's job in production (end of the HTTP request's unit of work);
        // do it explicitly here to observe the effect.
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        var target = await sut.GetMemberAsync(targetId, tenantId, CancellationToken.None);
        target.Should().BeNull();
    }
}
