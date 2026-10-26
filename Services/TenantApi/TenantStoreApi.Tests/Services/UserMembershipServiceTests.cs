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

public class UserMembershipServiceTests
{
    private static async Task<(UserMembershipService Sut, IUnitOfWorkService Uow)> CreateSutAsync()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        await new PermissionCatalogSeederService(uow).EnsureSeededAsync(CancellationToken.None);

        var roleService = new RoleService(uow);
        var sut = new UserMembershipService(uow, Mock.Of<IPublishEndpoint>(), roleService, new MembershipRoleService(uow));
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

    // ── JoinAsync ────────────────────────────────────────────────────────────────────────
    // Shared by the synchronous ActivateMembership gRPC call (invitation acceptance) and the
    // async UserJoin event that still arrives afterwards for the same join -- both run for
    // every acceptance, so whichever lands second must be a no-op, never a duplicate row.

    private static Task<int> CountMembershipsAsync(UserMembershipService sut, Guid tenantId) =>
        sut.Context.Memberships.CountAsync(x => x.TenantId == tenantId);

    [Fact]
    public async Task JoinAsync_ActivatesPendingInvitePlaceholder_InPlace()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await sut.CreateInvitePlaceholderAsync(tenantId, "Acme", "invitee@test.com", ["Employee"], "Invitee", CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        await sut.JoinAsync(userId, tenantId, "Acme", "invitee@test.com", "Invitee", ["Employee"], CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        var membership = await sut.GetMemberAsync(userId, tenantId, CancellationToken.None);
        membership.Should().NotBeNull();
        membership!.Status.Should().Be("Active");
        membership.HasRole("Employee").Should().BeTrue();
        (await CountMembershipsAsync(sut, tenantId)).Should().Be(1);
    }

    [Fact]
    public async Task JoinAsync_IsIdempotent_WhenCalledTwiceForTheSameJoin()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await sut.CreateInvitePlaceholderAsync(tenantId, "Acme", "invitee@test.com", ["Admin"], "Invitee", CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        await sut.JoinAsync(userId, tenantId, "Acme", "invitee@test.com", "Invitee", ["Admin"], CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);
        await sut.JoinAsync(userId, tenantId, "Acme", "invitee@test.com", "Invitee", ["Admin"], CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        (await CountMembershipsAsync(sut, tenantId)).Should().Be(1);
    }

    [Fact]
    public async Task JoinAsync_ReactivatesRevokedMembership_AndDropsItsNewPlaceholder()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var revoked = await AddMemberAsync(sut, tenantId, userId, "None");
        revoked.Status = "Revoked";
        await sut.Context.SaveChangesAsync(CancellationToken.None);
        await sut.CreateInvitePlaceholderAsync(tenantId, "Acme", "invitee@test.com", ["None"], "Invitee", CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        await sut.JoinAsync(userId, tenantId, "Acme", "invitee@test.com", "Invitee", ["None"], CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        var membership = await sut.GetMemberAsync(userId, tenantId, CancellationToken.None);
        membership!.Status.Should().Be("Active");
        (await sut.FindPendingInviteAsync(tenantId, "invitee@test.com", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task JoinAsync_CreatesMembershipWithGivenRoles_WhenNoPlaceholderExists()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await sut.JoinAsync(userId, tenantId, "Acme", "someone@test.com", "Someone", ["Admin"], CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        var membership = await sut.GetMemberAsync(userId, tenantId, CancellationToken.None);
        membership!.HasRole("Admin").Should().BeTrue();
        membership.EffectivePermissionCodes().Should().NotBeEmpty();
    }

    [Fact]
    public async Task JoinAsync_DefaultsToMember_WhenNoRolesGiven()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await sut.JoinAsync(userId, tenantId, "Acme", null, null, [], CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        var membership = await sut.GetMemberAsync(userId, tenantId, CancellationToken.None);
        membership!.RoleNames().Should().Equal("None");
    }

    [Fact]
    public async Task ReplaceRolesAsync_Succeeds_WhenCallerIsOwner()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, ownerId, "Owner");
        var targetMembership = await AddMemberAsync(sut, tenantId, targetId, "None");

        await sut.ReplaceRolesAsync(ownerId, targetMembership.Id, tenantId, ["Admin"], CancellationToken.None);

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
        var targetMembership = await AddMemberAsync(sut, tenantId, targetId, "None", "Admin");

        await sut.ReplaceRolesAsync(ownerId, targetMembership.Id, tenantId, ["Admin"], CancellationToken.None);

        var target = await sut.GetMemberAsync(targetId, tenantId, CancellationToken.None);
        target!.HasRole("Admin").Should().BeTrue();
        target!.HasRole("None").Should().BeFalse();
        target!.Roles.Should().ContainSingle();
    }

    /// <summary>
    /// Regression guard for a production 500: "Duplicate entry '...-Employee' for key
    /// 'membershiproles.IX_MembershipRoles_UserMembershipId_Role'". Two independent
    /// IUnitOfWorkService instances (own DbContext, own identity map, sharing the same
    /// underlying store) stand in for two genuinely concurrent requests -- one grants "Employee"
    /// via AddRoleAsync and commits, then a second, unrelated ReplaceRolesAsync call that also
    /// wants the member to end up with "Employee" must not blow up just because that role
    /// already exists in the store.
    /// </summary>
    [Fact]
    public async Task ReplaceRolesAsync_DoesNotThrow_WhenARequestedRoleWasAlreadyGrantedElsewhere()
    {
        var dbName = Guid.NewGuid().ToString();
        var uow1 = TenantTestContextFactory.CreateUnitOfWork(TenantTestContextFactory.CreateContext(dbName));
        await new PermissionCatalogSeederService(uow1).EnsureSeededAsync(CancellationToken.None);
        var sut1 = new UserMembershipService(uow1, Mock.Of<IPublishEndpoint>(), new RoleService(uow1), new MembershipRoleService(uow1));

        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await AddMemberAsync(sut1, tenantId, ownerId, "Owner");
        var targetMembership = await AddMemberAsync(sut1, tenantId, targetId, "None");

        // A second, independent request-scoped service instance grants "Employee" and commits.
        var uow2 = TenantTestContextFactory.CreateUnitOfWork(TenantTestContextFactory.CreateContext(dbName));
        var sut2 = new UserMembershipService(uow2, Mock.Of<IPublishEndpoint>(), new RoleService(uow2), new MembershipRoleService(uow2));
        await sut2.AddRoleAsync(ownerId, targetMembership.Id, tenantId, "Employee", CancellationToken.None);
        await sut2.Context.SaveChangesAsync(CancellationToken.None);

        var act = () => sut1.ReplaceRolesAsync(ownerId, targetMembership.Id, tenantId, ["None", "Employee"], CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReplaceRolesAsync_ThrowsUnauthorized_WhenCallerIsPlainMember()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, memberId, "None");
        var targetMembership = await AddMemberAsync(sut, tenantId, targetId, "None");

        var act = () => sut.ReplaceRolesAsync(memberId, targetMembership.Id, tenantId, ["Admin"], CancellationToken.None);

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
        var targetMembership = await AddMemberAsync(sut, tenantId, targetId, "None");

        await sut.ReplaceRolesAsync(adminId, targetMembership.Id, tenantId, ["Admin"], CancellationToken.None);

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
        await AddMemberAsync(sut, tenantId, memberId, "None");
        var targetMembership = await AddMemberAsync(sut, tenantId, targetId, "None");

        var act = () => sut.RemoveMemberAsync(memberId, targetMembership.Id, tenantId, CancellationToken.None);

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
        await AddMemberAsync(sut, tenantId, userId, "None", "Payroll Approver");

        var membership = await sut.GetMemberAsync(userId, tenantId, CancellationToken.None);

        membership!.EffectivePermissionCodes().Should().Contain("Payroll Run:Approve");
    }

    [Fact]
    public async Task HasPermission_ReturnsFalse_ForMemberWithNoGrantedPermissions()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, userId, "None");

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
        await AddMemberAsync(sut, tenantId, callerId, "None", "User Manager");
        var targetMembership = await AddMemberAsync(sut, tenantId, targetId, "None");

        // Employee: a baseline role, grantable by anyone who may edit roles (RoleGrantPolicy).
        await sut.ReplaceRolesAsync(callerId, targetMembership.Id, tenantId, ["Employee"], CancellationToken.None);

        var target = await sut.GetMemberAsync(targetId, tenantId, CancellationToken.None);
        target!.HasRole("Employee").Should().BeTrue();
    }

    // ── No privilege escalation (RoleGrantPolicy) ──────────────────────────────────────────

    private static async Task<Guid> AddUserManagerAsync(UserMembershipService sut, IUnitOfWorkService uow, Guid tenantId)
    {
        var roleService = new RoleService(uow);
        var customRole = await roleService.AddCustomRoleAsync(tenantId, "User Manager", "", CancellationToken.None);
        var permission = await uow.Context.Permissions.SingleAsync(p => p.Code == "Users:Edit");
        await roleService.SetPermissionsAsync(customRole.Id, [permission.Id], CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);
        var callerId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, callerId, "None", "User Manager");
        return callerId;
    }

    [Fact]
    public async Task ReplaceRolesAsync_Rejects_ARoleWithPermissionsTheCallerDoesNotHold()
    {
        var (sut, uow) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var callerId = await AddUserManagerAsync(sut, uow, tenantId);
        var targetMembership = await AddMemberAsync(sut, tenantId, Guid.NewGuid(), "None");

        var act = () => sut.ReplaceRolesAsync(callerId, targetMembership.Id, tenantId, ["Admin"], CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*'Admin'*permissions you don't have*");
    }

    [Fact]
    public async Task AddRoleAsync_Rejects_ARoleWithPermissionsTheCallerDoesNotHold()
    {
        var (sut, uow) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var callerId = await AddUserManagerAsync(sut, uow, tenantId);
        var targetMembership = await AddMemberAsync(sut, tenantId, Guid.NewGuid(), "None");

        var act = () => sut.AddRoleAsync(callerId, targetMembership.Id, tenantId, "Admin", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReplaceRolesAsync_LetsTheTargetKeepARoleTheCallerCouldNotGrant()
    {
        var (sut, uow) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var callerId = await AddUserManagerAsync(sut, uow, tenantId);
        var targetId = Guid.NewGuid();
        var targetMembership = await AddMemberAsync(sut, tenantId, targetId, "Admin", "None");

        await sut.ReplaceRolesAsync(callerId, targetMembership.Id, tenantId, ["Admin", "Employee"], CancellationToken.None);

        var target = await sut.GetMemberAsync(targetId, tenantId, CancellationToken.None);
        target!.RoleNames().Should().BeEquivalentTo(["Admin", "Employee"]);
    }

    [Fact]
    public async Task Admin_CanGrantAdmin_ButNotARoleHoldingConfidentialAccess()
    {
        var (sut, uow) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var roleService = new RoleService(uow);
        var cleared = await roleService.AddCustomRoleAsync(tenantId, "Confidential HR", "", CancellationToken.None);
        var access = await uow.Context.Permissions.SingleAsync(p => p.Code == "Confidential Records:Access");
        await roleService.SetPermissionsAsync(cleared.Id, [access.Id], CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);
        var adminId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, adminId, "Admin");
        var admin = await sut.GetMemberAsync(adminId, tenantId, CancellationToken.None);

        var grantable = (await roleService.GrantableRolesAsync(admin!, tenantId, CancellationToken.None)).Select(r => r.Name).ToList();

        grantable.Should().Contain(["Admin", "None", "Employee"]);
        grantable.Should().NotContain(["Owner", "Confidential HR"]);
    }

    [Fact]
    public async Task Owner_CanGrantEveryRoleButOwner()
    {
        var (sut, uow) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var roleService = new RoleService(uow);
        await roleService.AddCustomRoleAsync(tenantId, "Anything", "", CancellationToken.None);
        await uow.Context.SaveChangesAsync(CancellationToken.None);
        var ownerId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, ownerId, "Owner");
        var owner = await sut.GetMemberAsync(ownerId, tenantId, CancellationToken.None);

        var grantable = (await roleService.GrantableRolesAsync(owner!, tenantId, CancellationToken.None)).Select(r => r.Name).ToList();

        grantable.Should().Contain(["Admin", "Anything"]).And.NotContain("Owner");
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
        await AddMemberAsync(sut, tenantId, callerId, "None", "User Remover");
        var targetMembership = await AddMemberAsync(sut, tenantId, targetId, "None");

        await sut.RemoveMemberAsync(callerId, targetMembership.Id, tenantId, CancellationToken.None);
        // RemoveMemberAsync itself only marks the entity removed (soft-delete interceptor) --
        // flushing is the caller's job in production (end of the HTTP request's unit of work);
        // do it explicitly here to observe the effect.
        await uow.Context.SaveChangesAsync(CancellationToken.None);

        var target = await sut.GetMemberAsync(targetId, tenantId, CancellationToken.None);
        target.Should().BeNull();
    }

    // ── RevokeForSeparatedEmployeeAsync ──────────────────────────────────────────────────
    // Driven by hrms-api when an employee is Terminated/Resigned/Retired/Deceased -- no caller,
    // so no permission checks; the Owner is never locked out this way.

    [Fact]
    public async Task RevokeForSeparatedEmployeeAsync_RevokesAnActiveMemberAndAnnouncesIt()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        await new PermissionCatalogSeederService(uow).EnsureSeededAsync(CancellationToken.None);
        var publisher = new Mock<IPublishEndpoint>();
        var sut = new UserMembershipService(uow, publisher.Object, new RoleService(uow), new MembershipRoleService(uow));
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, userId, "Employee");

        var revoked = await sut.RevokeForSeparatedEmployeeAsync(userId, tenantId, CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        revoked.Should().BeTrue();
        (await sut.GetMemberAsync(userId, tenantId, CancellationToken.None))!.Status.Should().Be("Revoked");
        publisher.Verify(p => p.Publish(
            It.Is<MembershipChanged>(m => m.UserId == userId && m.TenantId == tenantId
                && m.ChangeType == "StatusChanged" && m.NewStatus == "Revoked"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeForSeparatedEmployeeAsync_LeavesTheOwnerAlone()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, userId, "Owner");

        (await sut.RevokeForSeparatedEmployeeAsync(userId, tenantId, CancellationToken.None)).Should().BeFalse();

        (await sut.GetMemberAsync(userId, tenantId, CancellationToken.None))!.Status.Should().NotBe("Revoked");
    }

    [Fact]
    public async Task RevokeForSeparatedEmployeeAsync_IsIdempotentAndIgnoresUnknownAccounts()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, userId, "Employee");

        (await sut.RevokeForSeparatedEmployeeAsync(userId, tenantId, CancellationToken.None)).Should().BeTrue();
        await sut.Context.SaveChangesAsync(CancellationToken.None);
        (await sut.RevokeForSeparatedEmployeeAsync(userId, tenantId, CancellationToken.None)).Should().BeFalse();
        (await sut.RevokeForSeparatedEmployeeAsync(Guid.NewGuid(), tenantId, CancellationToken.None)).Should().BeFalse();
    }

    // ── AcceptInviteAsync / InviteRevoked ────────────────────────────────────────────────
    // An admin who revokes or removes a pending invite must stop the link from working: plain
    // JoinAsync would create a brand new Active membership, so the invitation flow goes through
    // AcceptInviteAsync, which refuses unless the invite is still pending.

    private static async Task<(UserMembershipService Sut, Mock<IPublishEndpoint> Publisher)> CreateSutWithPublisherAsync()
    {
        var uow = TenantTestContextFactory.CreateUnitOfWork();
        await new PermissionCatalogSeederService(uow).EnsureSeededAsync(CancellationToken.None);
        var publisher = new Mock<IPublishEndpoint>();
        var sut = new UserMembershipService(uow, publisher.Object, new RoleService(uow), new MembershipRoleService(uow));
        return (sut, publisher);
    }

    private static async Task<UserMembership> AddPlaceholderAsync(UserMembershipService sut, Guid tenantId, string email = "invitee@test.com")
    {
        await sut.CreateInvitePlaceholderAsync(tenantId, "Acme", email, ["Employee"], "Invitee", CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);
        return (await sut.FindPendingInviteAsync(tenantId, email, CancellationToken.None))!;
    }

    // FindPendingInviteAsync reads no-tracking, so the tracked row is looked up by key to change it.
    private static async Task RevokePlaceholderAsync(UserMembershipService sut, UserMembership placeholder)
    {
        var tracked = await sut.Context.Memberships.FindAsync(placeholder.Id);
        tracked!.Status = "Revoked";
        await sut.Context.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AcceptInviteAsync_JoinsThePendingInvite()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await AddPlaceholderAsync(sut, tenantId);

        var (membership, denyReason) = await sut.AcceptInviteAsync(
            userId, tenantId, "Acme", "invitee@test.com", "Invitee", ["Employee"], replay: false, CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        denyReason.Should().BeNull();
        membership.Should().NotBeNull();
        (await sut.GetMemberAsync(userId, tenantId, CancellationToken.None))!.Status.Should().Be("Active");
        (await CountMembershipsAsync(sut, tenantId)).Should().Be(1);
    }

    [Fact]
    public async Task AcceptInviteAsync_IsDenied_AfterTheInviteWasRevoked_AndCreatesNothing()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var placeholder = await AddPlaceholderAsync(sut, tenantId);
        await RevokePlaceholderAsync(sut, placeholder);

        var (membership, denyReason) = await sut.AcceptInviteAsync(
            userId, tenantId, "Acme", "invitee@test.com", "Invitee", ["Employee"], replay: false, CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        membership.Should().BeNull();
        denyReason.Should().NotBeNullOrEmpty();
        (await sut.GetMemberAsync(userId, tenantId, CancellationToken.None)).Should().BeNull();
        (await CountMembershipsAsync(sut, tenantId)).Should().Be(1); // just the revoked placeholder
    }

    [Fact]
    public async Task AcceptInviteAsync_IsDenied_AfterTheInviteWasRemoved()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, ownerId, "Owner");
        var placeholder = await AddPlaceholderAsync(sut, tenantId);
        await sut.RemoveMemberAsync(ownerId, placeholder.Id, tenantId, CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        var (membership, _) = await sut.AcceptInviteAsync(
            Guid.NewGuid(), tenantId, "Acme", "invitee@test.com", "Invitee", ["Employee"], replay: false, CancellationToken.None);

        membership.Should().BeNull();
    }

    [Fact]
    public async Task AcceptInviteAsync_ReactivatesARevokedMember_OnlyWhileTheNewInviteIsLive()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var old = await AddMemberAsync(sut, tenantId, userId, "None");
        old.Status = "Revoked";
        await AddPlaceholderAsync(sut, tenantId);

        var (live, _) = await sut.AcceptInviteAsync(
            userId, tenantId, "Acme", "invitee@test.com", "Invitee", ["None"], replay: false, CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);
        live.Should().NotBeNull();
        (await sut.GetMemberAsync(userId, tenantId, CancellationToken.None))!.Status.Should().Be("Active");

        // Revoked again, with a new invite that the admin also revokes: the link must stay dead.
        var member = (await sut.GetMemberAsync(userId, tenantId, CancellationToken.None))!;
        member.Status = "Revoked";
        var second = await AddPlaceholderAsync(sut, tenantId);
        await RevokePlaceholderAsync(sut, second);

        var (denied, _) = await sut.AcceptInviteAsync(
            userId, tenantId, "Acme", "invitee@test.com", "Invitee", ["None"], replay: false, CancellationToken.None);
        denied.Should().BeNull();
        (await sut.GetMemberAsync(userId, tenantId, CancellationToken.None))!.Status.Should().Be("Revoked");
    }

    [Fact]
    public async Task AcceptInviteAsync_RetryByAnAlreadyActiveMember_StillWorks()
    {
        // A first accept that activated the membership but failed afterwards (the placeholder is
        // already consumed) must be retryable.
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, userId, "Employee");

        var (membership, denyReason) = await sut.AcceptInviteAsync(
            userId, tenantId, "Acme", "invitee@test.com", "Invitee", ["Employee"], replay: false, CancellationToken.None);

        membership.Should().NotBeNull();
        denyReason.Should().BeNull();
    }

    [Fact]
    public async Task AcceptInviteAsync_Replay_NeedsAnActiveMembership_AndNeverReactivates()
    {
        var (sut, _) = await CreateSutAsync();
        var tenantId = Guid.NewGuid();
        var active = Guid.NewGuid();
        var revoked = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, active, "Employee");
        (await AddMemberAsync(sut, tenantId, revoked, "Employee")).Status = "Revoked";
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        var (ok, _) = await sut.AcceptInviteAsync(active, tenantId, "Acme", "a@test.com", "A", ["Employee"], replay: true, CancellationToken.None);
        var (denied, reason) = await sut.AcceptInviteAsync(revoked, tenantId, "Acme", "b@test.com", "B", ["Employee"], replay: true, CancellationToken.None);
        var (unknown, _) = await sut.AcceptInviteAsync(Guid.NewGuid(), tenantId, "Acme", "c@test.com", "C", ["Employee"], replay: true, CancellationToken.None);
        await sut.Context.SaveChangesAsync(CancellationToken.None);

        ok.Should().NotBeNull();
        denied.Should().BeNull();
        reason.Should().NotBeNullOrEmpty();
        unknown.Should().BeNull();
        (await sut.GetMemberAsync(revoked, tenantId, CancellationToken.None))!.Status.Should().Be("Revoked");
    }

    [Fact]
    public async Task UpdateStatusAsync_RevokingAPendingInvite_PublishesInviteRevoked()
    {
        var (sut, publisher) = await CreateSutWithPublisherAsync();
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, ownerId, "Owner");
        var placeholder = await AddPlaceholderAsync(sut, tenantId);

        await sut.UpdateStatusAsync(ownerId, placeholder.Id, tenantId, "Revoked", CancellationToken.None);

        publisher.Verify(p => p.Publish(
            It.Is<InviteRevoked>(m => m.TenantId == tenantId && m.Email == "invitee@test.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_RevokingARealMember_DoesNotPublishInviteRevoked()
    {
        var (sut, publisher) = await CreateSutWithPublisherAsync();
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, ownerId, "Owner");
        var target = await AddMemberAsync(sut, tenantId, Guid.NewGuid(), "Employee");

        await sut.UpdateStatusAsync(ownerId, target.Id, tenantId, "Revoked", CancellationToken.None);

        publisher.Verify(p => p.Publish(It.IsAny<InviteRevoked>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveMemberAsync_RemovingAPendingInvite_PublishesInviteRevoked()
    {
        var (sut, publisher) = await CreateSutWithPublisherAsync();
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await AddMemberAsync(sut, tenantId, ownerId, "Owner");
        var placeholder = await AddPlaceholderAsync(sut, tenantId);

        await sut.RemoveMemberAsync(ownerId, placeholder.Id, tenantId, CancellationToken.None);

        publisher.Verify(p => p.Publish(
            It.Is<InviteRevoked>(m => m.TenantId == tenantId && m.Email == "invitee@test.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
