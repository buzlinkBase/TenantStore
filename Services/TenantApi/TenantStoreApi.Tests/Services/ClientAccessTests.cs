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
/// Client-portal access held in TenantApi: who belongs to a client, what roles they hold there, and
/// the safeguards the portal used to enforce itself (keep a Client Admin, no self-edits, no granting
/// what you don't hold), plus isolation between clients.
/// </summary>
public class ClientAccessTests
{
    internal sealed class Fixture
    {
        public required IUnitOfWorkService Uow { get; init; }
        public required Mock<IPublishEndpoint> Publisher { get; init; }
        public required ClientAccessService Access { get; init; }
        public required ClientMembershipService Members { get; init; }
        public required ClientRoleService Roles { get; init; }
        public required ClientEntityService Entities { get; init; }
        public required ClientAccessImportService Import { get; init; }
        public required UserMembershipService Staff { get; init; }
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid ClientId { get; private set; }

        public ClientCaller Agency { get; } = ClientCaller.Agency(Guid.NewGuid());

        public async Task SaveAsync() => await Uow.Context.SaveChangesAsync();

        public async Task<Guid> RoleIdAsync(string name) =>
            (await Uow.Context.Roles.FirstAsync(r => r.Name == name && r.IsSystemRole && r.Audience == RoleAudience.Client)).Id;

        public async Task<Guid> NewClientAsync(string name = "Client A")
        {
            var id = Guid.NewGuid();
            await Entities.UpsertAsync(TenantId, id, null, name, true);
            await SaveAsync();
            return id;
        }

        public async Task<UserMembership> GrantAsync(Guid userId, Guid clientId, params string[] roleNames)
        {
            var ids = new List<Guid>();
            foreach (var name in roleNames) ids.Add(await RoleIdAsync(name));
            var membership = await Members.GrantAccessAsync(userId, clientId, $"{userId}@x.com", "Person", ids);
            await SaveAsync();
            return membership;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var uow = TenantTestContextFactory.CreateUnitOfWork();
            await new PermissionCatalogSeederService(uow, [new HrmsPermissionCatalog(), new ClientPortalPermissionCatalog()])
                .EnsureSeededAsync(CancellationToken.None);

            var publisher = new Mock<IPublishEndpoint>();
            var staff = new UserMembershipService(uow, publisher.Object, new RoleService(uow), new MembershipRoleService(uow));
            var access = new ClientAccessService(uow, staff);
            var members = new ClientMembershipService(uow, access, publisher.Object);
            var roles = new ClientRoleService(uow, access, publisher.Object);
            var fixture = new Fixture
            {
                Uow = uow,
                Publisher = publisher,
                Staff = staff,
                Access = access,
                Members = members,
                Roles = roles,
                Entities = new ClientEntityService(uow),
                Import = new ClientAccessImportService(uow, access, roles, members),
            };
            uow.Context.Tenants.Add(new Tenant { Id = fixture.TenantId, TenantName = "Agency" });
            await uow.Context.SaveChangesAsync();
            fixture.ClientId = await fixture.NewClientAsync();
            return fixture;
        }
    }

    // ── Resolving access ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_ReturnsTheRolesAndPermissionsHeldInThatClient()
    {
        var f = await Fixture.CreateAsync();
        var user = Guid.NewGuid();
        await f.GrantAsync(user, f.ClientId, ClientRoles.Executive);

        var access = await f.Access.ResolveAsync(user, f.ClientId);

        access.Should().NotBeNull();
        access!.IsActive.Should().BeTrue();
        access.Roles.Should().Equal(ClientRoles.Executive);
        access.Permissions.Should().Contain("clientportal:Users:View").And.NotContain("clientportal:Roles:Manage");
        access.TenantName.Should().Be("Agency");
    }

    [Fact]
    public async Task Resolve_IsNullForSomeoneWithNoStandingInThatClient()
    {
        var f = await Fixture.CreateAsync();
        var otherClient = await f.NewClientAsync("Client B");
        var user = Guid.NewGuid();
        await f.GrantAsync(user, otherClient, ClientRoles.Admin);

        (await f.Access.ResolveAsync(user, f.ClientId)).Should().BeNull();
        (await f.Access.ResolveAsync(Guid.NewGuid(), f.ClientId)).Should().BeNull();
        (await f.Access.ResolveAsync(user, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Resolve_IgnoresCompanyStaffMemberships()
    {
        var f = await Fixture.CreateAsync();
        var staffUser = Guid.NewGuid();
        await f.Staff.AddAsync(new UserMembership { TenantId = f.TenantId, UserId = staffUser }, [TenantRoles.Owner], CancellationToken.None);
        await f.SaveAsync();

        (await f.Access.ResolveAsync(staffUser, f.ClientId)).Should().BeNull();
    }

    [Fact]
    public async Task ListForUser_SpansEveryClient_AndKeepsRolesPerClient()
    {
        var f = await Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        var user = Guid.NewGuid();
        await f.GrantAsync(user, f.ClientId, ClientRoles.Admin);
        await f.GrantAsync(user, clientB, ClientRoles.FinanceManager);

        var list = await f.Access.ListForUserAsync(user);

        list.Should().HaveCount(2);
        list.Single(a => a.ClientEntityId == f.ClientId).Roles.Should().Equal(ClientRoles.Admin);
        list.Single(a => a.ClientEntityId == clientB).Roles.Should().Equal(ClientRoles.FinanceManager);
    }

    [Fact]
    public async Task Resolve_InactiveLinkOrInactiveClient_ReturnsNoPermissions()
    {
        var f = await Fixture.CreateAsync();
        var user = Guid.NewGuid();
        await f.GrantAsync(user, f.ClientId, ClientRoles.Admin);
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin); // a second admin so the first may be switched off
        var membership = await f.Uow.Context.Memberships.FirstAsync(m => m.UserId == user);

        await f.Members.SetStatusAsync(f.Agency, f.ClientId, membership.Id, "Inactive");
        await f.SaveAsync();
        var off = await f.Access.ResolveAsync(user, f.ClientId);
        off!.Status.Should().Be("Inactive");
        off.Permissions.Should().BeEmpty();

        await f.Members.SetStatusAsync(f.Agency, f.ClientId, membership.Id, "Active");
        await f.Entities.UpsertAsync(f.TenantId, f.ClientId, null, "Client A", active: false);
        await f.SaveAsync();
        var clientOff = await f.Access.ResolveAsync(user, f.ClientId);
        clientOff!.IsActive.Should().BeFalse();
        clientOff.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GrantAccess_IsIdempotent_AndReactivatesARevokedMembership()
    {
        var f = await Fixture.CreateAsync();
        var user = Guid.NewGuid();
        var first = await f.GrantAsync(user, f.ClientId, ClientRoles.Executive);
        first.Status = "Revoked";
        await f.SaveAsync();

        await f.GrantAsync(user, f.ClientId, ClientRoles.Executive);

        (await f.Uow.Context.Memberships.CountAsync(m => m.UserId == user)).Should().Be(1);
        (await f.Uow.Context.ClientMembershipRoles.CountAsync(l => l.UserMembership.UserId == user)).Should().Be(1);
        (await f.Access.ResolveAsync(user, f.ClientId))!.IsActive.Should().BeTrue();
        first.Category.Should().Be(MembershipCategory.ClientUser);
    }

    [Fact]
    public async Task GrantAccess_RejectsARoleThatIsNotAClientRole()
    {
        var f = await Fixture.CreateAsync();
        var owner = await f.Uow.Context.Roles.FirstAsync(r => r.Name == "Owner");

        var act = () => f.Members.GrantAccessAsync(Guid.NewGuid(), f.ClientId, null, null, [owner.Id]);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Entry door for company staff ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AgencyCaller_RequiresAnActiveStaffMemberHoldingClientManage()
    {
        var f = await Fixture.CreateAsync();
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        await f.Staff.AddAsync(new UserMembership { TenantId = f.TenantId, UserId = owner }, [TenantRoles.Owner], CancellationToken.None);
        await f.Staff.AddAsync(new UserMembership { TenantId = f.TenantId, UserId = member }, [TenantRoles.Member], CancellationToken.None);
        await f.SaveAsync();

        (await f.Access.AgencyCallerAsync(owner, f.TenantId, f.ClientId)).IsAgencyStaff.Should().BeTrue();
        await ((Func<Task>)(() => f.Access.AgencyCallerAsync(member, f.TenantId, f.ClientId))).Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task AgencyCaller_ACompanysStaffCannotReachAnotherCompanysClient()
    {
        var f = await Fixture.CreateAsync();
        var otherTenant = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        await f.Staff.AddAsync(new UserMembership { TenantId = otherTenant, UserId = outsider }, [TenantRoles.Owner], CancellationToken.None);
        await f.SaveAsync();

        var act = () => f.Access.AgencyCallerAsync(outsider, otherTenant, f.ClientId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Changing roles ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetRoles_ReplacesTheRolesWithinThatClient_AndAnnouncesIt()
    {
        var f = await Fixture.CreateAsync();
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var user = Guid.NewGuid();
        var membership = await f.GrantAsync(user, f.ClientId, ClientRoles.FinanceManager);

        await f.Members.SetRolesAsync(f.Agency, f.ClientId, membership.Id, [await f.RoleIdAsync(ClientRoles.SiteSupervisor)]);
        await f.SaveAsync();

        (await f.Access.ResolveAsync(user, f.ClientId))!.Roles.Should().Equal(ClientRoles.SiteSupervisor);
        f.Publisher.Verify(p => p.Publish(
            It.Is<ClientMembershipChanged>(m => m.UserId == user && m.ClientEntityId == f.ClientId && m.TenantId == f.TenantId && m.ChangeType == "RolesChanged"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SetRoles_RolesAtOneClientDoNotTouchTheSamePersonsOtherClient()
    {
        var f = await Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var user = Guid.NewGuid();
        var membership = await f.GrantAsync(user, f.ClientId, ClientRoles.FinanceManager);
        await f.GrantAsync(user, clientB, ClientRoles.Executive);

        await f.Members.SetRolesAsync(f.Agency, f.ClientId, membership.Id, [await f.RoleIdAsync(ClientRoles.SiteSupervisor)]);
        await f.SaveAsync();

        (await f.Access.ResolveAsync(user, f.ClientId))!.Roles.Should().Equal(ClientRoles.SiteSupervisor);
        (await f.Access.ResolveAsync(user, clientB))!.Roles.Should().Equal(ClientRoles.Executive);
    }

    [Fact]
    public async Task SetRoles_CannotTakeTheLastClientAdminOffAdmin_ButCanOnceAnotherExists()
    {
        var f = await Fixture.CreateAsync();
        var sole = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var finance = new[] { await f.RoleIdAsync(ClientRoles.FinanceManager) };

        var act = () => f.Members.SetRolesAsync(f.Agency, f.ClientId, sole.Id, finance);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Client Admin*");

        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        await f.Members.SetRolesAsync(f.Agency, f.ClientId, sole.Id, finance);
        await f.SaveAsync();
        (await f.Access.ResolveAsync(sole.UserId, f.ClientId))!.Roles.Should().Equal(ClientRoles.FinanceManager);
    }

    [Fact]
    public async Task SetRoles_AnAdminWhoIsSwitchedOffDoesNotCountAsTheOtherAdmin()
    {
        var f = await Fixture.CreateAsync();
        var sole = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var dormant = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        await f.Members.SetStatusAsync(f.Agency, f.ClientId, dormant.Id, "Inactive");
        await f.SaveAsync();

        var act = () => f.Members.SetRolesAsync(f.Agency, f.ClientId, sole.Id, [f.RoleIdAsync(ClientRoles.FinanceManager).Result]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Client Admin*");
    }

    [Fact]
    public async Task SetRoles_NobodyChangesTheirOwnRoles()
    {
        var f = await Fixture.CreateAsync();
        var me = Guid.NewGuid();
        var mine = await f.GrantAsync(me, f.ClientId, ClientRoles.Admin);
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);

        var act = () => f.Members.SetRolesAsync(ClientCaller.ClientMember(me), f.ClientId, mine.Id, [f.RoleIdAsync(ClientRoles.Executive).Result]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*own roles*");
    }

    [Fact]
    public async Task SetRoles_RequiresAtLeastOneRole_AndRolesThisClientMayHold()
    {
        var f = await Fixture.CreateAsync();
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var target = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Executive);
        var owner = await f.Uow.Context.Roles.FirstAsync(r => r.Name == "Owner");

        await ((Func<Task>)(() => f.Members.SetRolesAsync(f.Agency, f.ClientId, target.Id, []))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => f.Members.SetRolesAsync(f.Agency, f.ClientId, target.Id, [owner.Id]))).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetRoles_AClientMemberCannotGrantARoleCarryingPermissionsTheyLack()
    {
        var f = await Fixture.CreateAsync();
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var viewAndAssign = await CustomRoleAsync(f, "Assigner", "clientportal:Users:View", "clientportal:Users:AssignRoles");
        var assigner = Guid.NewGuid();
        await f.Members.GrantAccessAsync(assigner, f.ClientId, null, null, [viewAndAssign]);
        var target = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.SiteSupervisor);
        await f.SaveAsync();
        var caller = ClientCaller.ClientMember(assigner);

        // Finance Manager carries Invoices/Payments permissions the assigner doesn't hold.
        var tooMuch = () => f.Members.SetRolesAsync(caller, f.ClientId, target.Id, [f.RoleIdAsync(ClientRoles.FinanceManager).Result]);
        await tooMuch.Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't grant*");

        // A role within what they hold is fine.
        await f.Members.SetRolesAsync(caller, f.ClientId, target.Id, [viewAndAssign]);
        await f.SaveAsync();
        (await f.Access.ResolveAsync(target.UserId, f.ClientId))!.Roles.Should().Equal("Assigner");
    }

    [Fact]
    public async Task ClientMember_WithoutThePermission_IsRefused()
    {
        var f = await Fixture.CreateAsync();
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var exec = Guid.NewGuid(); // Client Executive: Users:View only
        await f.GrantAsync(exec, f.ClientId, ClientRoles.Executive);
        var target = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.SiteSupervisor);
        var caller = ClientCaller.ClientMember(exec);

        (await f.Members.ListMembersAsync(caller, f.ClientId)).Should().HaveCount(3);
        await ((Func<Task>)(() => f.Members.SetRolesAsync(caller, f.ClientId, target.Id, [f.RoleIdAsync(ClientRoles.Executive).Result])))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(() => f.Members.SetStatusAsync(caller, f.ClientId, target.Id, "Inactive")))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(() => f.Members.RemoveFromClientAsync(caller, f.ClientId, target.Id)))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ClientAdminOfOneClient_CannotManageAnotherClient()
    {
        var f = await Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        var adminOfB = Guid.NewGuid();
        await f.GrantAsync(adminOfB, clientB, ClientRoles.Admin);
        var target = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Executive);
        var caller = ClientCaller.ClientMember(adminOfB);

        await ((Func<Task>)(() => f.Members.ListMembersAsync(caller, f.ClientId))).Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(() => f.Members.SetRolesAsync(caller, f.ClientId, target.Id, [f.RoleIdAsync(ClientRoles.Admin).Result])))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task SetRoles_AMembershipOfAnotherClientIsNotFound()
    {
        var f = await Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        var inB = await f.GrantAsync(Guid.NewGuid(), clientB, ClientRoles.Admin);

        var act = () => f.Members.SetRolesAsync(f.Agency, f.ClientId, inB.Id, [f.RoleIdAsync(ClientRoles.Executive).Result]);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── Switching a person off, removing them ────────────────────────────────────────────────

    [Fact]
    public async Task SetStatus_DeactivatingTheLastAdminOrYourself_IsRefused()
    {
        var f = await Fixture.CreateAsync();
        var me = Guid.NewGuid();
        var mine = await f.GrantAsync(me, f.ClientId, ClientRoles.Admin);

        await ((Func<Task>)(() => f.Members.SetStatusAsync(f.Agency, f.ClientId, mine.Id, "Inactive")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Client Admin*");

        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        await ((Func<Task>)(() => f.Members.SetStatusAsync(ClientCaller.ClientMember(me), f.ClientId, mine.Id, "Inactive")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*yourself*");
        await ((Func<Task>)(() => f.Members.SetStatusAsync(f.Agency, f.ClientId, mine.Id, "Revoked")))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Remove_TakesThePersonOutOfThatClientOnly_AndDropsTheMembershipWhenNothingIsLeft()
    {
        var f = await Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var user = Guid.NewGuid();
        var membership = await f.GrantAsync(user, f.ClientId, ClientRoles.Executive);
        await f.GrantAsync(user, clientB, ClientRoles.Executive);

        await f.Members.RemoveFromClientAsync(f.Agency, f.ClientId, membership.Id);
        await f.SaveAsync();
        (await f.Access.ResolveAsync(user, f.ClientId)).Should().BeNull();
        (await f.Access.ResolveAsync(user, clientB)).Should().NotBeNull();
        (await f.Uow.Context.Memberships.AnyAsync(m => m.UserId == user)).Should().BeTrue();

        await f.Members.RemoveFromClientAsync(f.Agency, clientB, membership.Id);
        await f.SaveAsync();
        (await f.Uow.Context.Memberships.AnyAsync(m => m.UserId == user)).Should().BeFalse();
    }

    [Fact]
    public async Task ManagingUsers_WithoutEverythingAClientAdminHolds_CannotLockAClientAdminOut()
    {
        var f = await Fixture.CreateAsync();
        var admin = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin); // a second admin, so the last-admin rule isn't what refuses
        var userManager = await CustomRoleAsync(f, "User Manager", "clientportal:Users:View", "clientportal:Users:Manage", "clientportal:Users:AssignRoles");
        var manager = Guid.NewGuid();
        await f.Members.GrantAccessAsync(manager, f.ClientId, null, null, [userManager]);
        await f.SaveAsync();
        var caller = ClientCaller.ClientMember(manager);

        await ((Func<Task>)(() => f.Members.SetStatusAsync(caller, f.ClientId, admin.Id, "Inactive")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't change*");
        await ((Func<Task>)(() => f.Members.SetRolesAsync(caller, f.ClientId, admin.Id, [userManager])))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't change*");
        await ((Func<Task>)(() => f.Members.RemoveFromClientAsync(caller, f.ClientId, admin.Id)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't change*");
        (await f.Access.ResolveAsync(admin.UserId, f.ClientId))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AClientAdmin_CanRevokeAnotherAdminAndAnyoneBelow()
    {
        var f = await Fixture.CreateAsync();
        var me = Guid.NewGuid();
        await f.GrantAsync(me, f.ClientId, ClientRoles.Admin);
        var otherAdmin = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var exec = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Executive);
        var caller = ClientCaller.ClientMember(me);

        await f.Members.SetStatusAsync(caller, f.ClientId, exec.Id, "Inactive");
        await f.Members.SetStatusAsync(caller, f.ClientId, otherAdmin.Id, "Inactive");
        await f.SaveAsync();

        (await f.Access.ResolveAsync(exec.UserId, f.ClientId))!.IsActive.Should().BeFalse();
        (await f.Access.ResolveAsync(otherAdmin.UserId, f.ClientId))!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GrantAccess_InvitingSomeoneSwitchedOffAtThisClientBringsThemBack_ButNotAtAnotherClient()
    {
        var f = await Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        var user = Guid.NewGuid();
        var inA = await f.GrantAsync(user, f.ClientId, ClientRoles.Executive);
        await f.GrantAsync(user, clientB, ClientRoles.Executive);
        await f.Members.SetStatusAsync(f.Agency, f.ClientId, inA.Id, "Inactive");
        await f.Members.SetStatusAsync(f.Agency, clientB, inA.Id, "Inactive");
        await f.SaveAsync();

        await f.GrantAsync(user, f.ClientId, ClientRoles.Executive);

        (await f.Access.ResolveAsync(user, f.ClientId))!.IsActive.Should().BeTrue();
        (await f.Access.ResolveAsync(user, clientB))!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SwitchingAClientOffTakesEveryUserOff_AndOnBringsBackAllButThoseRevokedOnTheirOwn()
    {
        var f = await Fixture.CreateAsync();
        var admin = Guid.NewGuid();
        await f.GrantAsync(admin, f.ClientId, ClientRoles.Admin);
        var kept = Guid.NewGuid();
        await f.GrantAsync(kept, f.ClientId, ClientRoles.Executive);
        var revoked = Guid.NewGuid();
        var revokedMembership = await f.GrantAsync(revoked, f.ClientId, ClientRoles.Executive);
        await f.Members.SetStatusAsync(f.Agency, f.ClientId, revokedMembership.Id, "Inactive");
        await f.SaveAsync();

        await f.Entities.UpsertAsync(f.TenantId, f.ClientId, null, "Client A", active: false);
        await f.SaveAsync();
        foreach (var user in new[] { admin, kept, revoked })
            (await f.Access.ResolveAsync(user, f.ClientId))!.IsActive.Should().BeFalse();

        await f.Entities.UpsertAsync(f.TenantId, f.ClientId, null, "Client A", active: true);
        await f.SaveAsync();
        (await f.Access.ResolveAsync(admin, f.ClientId))!.IsActive.Should().BeTrue();
        (await f.Access.ResolveAsync(kept, f.ClientId))!.IsActive.Should().BeTrue();
        (await f.Access.ResolveAsync(revoked, f.ClientId))!.IsActive.Should().BeFalse("they were revoked on their own, not by the client being off");
    }

    [Fact]
    public async Task ListMembers_WhileTheClientIsOff_TellsRevokedOnTheirOwnApartFromBlockedByTheClient()
    {
        var f = await Fixture.CreateAsync();
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var kept = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Executive);
        var revoked = await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Executive);
        await f.Members.SetStatusAsync(f.Agency, f.ClientId, revoked.Id, "Inactive");
        await f.Entities.UpsertAsync(f.TenantId, f.ClientId, null, "Client A", active: false);
        await f.SaveAsync();

        var members = await f.Members.ListMembersAsync(f.Agency, f.ClientId);

        members.Should().OnlyContain(m => m.Status == "Inactive", "the whole client is off");
        members.Single(m => m.MembershipId == kept.Id).OwnStatus.Should().Be("Active");
        members.Single(m => m.MembershipId == revoked.Id).OwnStatus.Should().Be("Inactive");
    }

    [Fact]
    public async Task ListMembers_ShowsOnlyThatClientsPeople_WithTheirRoles()
    {
        var f = await Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        await f.GrantAsync(Guid.NewGuid(), clientB, ClientRoles.Admin);

        var members = await f.Members.ListMembersAsync(f.Agency, f.ClientId);

        members.Should().ContainSingle().Which.Roles.Select(r => r.Name).Should().Equal(ClientRoles.Admin);
    }

    // ── Custom roles ─────────────────────────────────────────────────────────────────────────

    private static async Task<Guid> CustomRoleAsync(Fixture f, string name, params string[] codes)
    {
        var permissionIds = await f.Uow.Context.Permissions.Where(p => codes.Contains(p.Code)).Select(p => p.Id).ToListAsync();
        var role = await f.Roles.CreateCustomRoleAsync(f.Agency, f.ClientId, name, "custom", permissionIds);
        await f.SaveAsync();
        return role.Id;
    }

    [Fact]
    public async Task CustomRole_IsScopedToItsClient_AndListedWithTheBuiltInOnes()
    {
        var f = await Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        var roleId = await CustomRoleAsync(f, "Night Auditor", "clientportal:Time Logs:View");

        var forA = await f.Roles.ListRolesAsync(f.Agency, f.ClientId);
        var forB = await f.Roles.ListRolesAsync(f.Agency, clientB);

        forA.Select(r => r.Name).Should().Contain(["Client Admin", "Night Auditor"]);
        forA.Single(r => r.Id == roleId).Permissions.Select(p => p.Code).Should().Equal("clientportal:Time Logs:View");
        forB.Select(r => r.Name).Should().Contain("Client Admin").And.NotContain("Night Auditor");

        // And the other client can neither assign nor edit it.
        await f.GrantAsync(Guid.NewGuid(), clientB, ClientRoles.Admin);
        var target = await f.GrantAsync(Guid.NewGuid(), clientB, ClientRoles.Executive);
        await ((Func<Task>)(() => f.Members.SetRolesAsync(f.Agency, clientB, target.Id, [roleId]))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => f.Roles.DeleteCustomRoleAsync(f.Agency, clientB, roleId))).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CustomRole_NameMustBeFreshAmongBuiltInAndOwnRoles_ButMayRepeatAcrossClients()
    {
        var f = await Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        await CustomRoleAsync(f, "Auditor");

        await ((Func<Task>)(() => f.Roles.CreateCustomRoleAsync(f.Agency, f.ClientId, "Auditor", "", [])))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => f.Roles.CreateCustomRoleAsync(f.Agency, f.ClientId, "Finance Manager", "", [])))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => f.Roles.CreateCustomRoleAsync(f.Agency, f.ClientId, "  ", "", [])))
            .Should().ThrowAsync<ArgumentException>();

        await f.Roles.CreateCustomRoleAsync(f.Agency, clientB, "Auditor", "", []);
        await f.SaveAsync();
    }

    [Fact]
    public async Task CustomRole_CanOnlyCarryClientSidePermissions_AndOnlyOnesTheEditorHolds()
    {
        var f = await Fixture.CreateAsync();
        var hrmsId = (await f.Uow.Context.Permissions.FirstAsync(p => p.Product == "hrms")).Id;
        var agencyBillingId = (await f.Uow.Context.Permissions.FirstAsync(p => p.Code == "clientportal:Billing:View")).Id;

        await ((Func<Task>)(() => f.Roles.CreateCustomRoleAsync(f.Agency, f.ClientId, "X", "", [hrmsId]))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => f.Roles.CreateCustomRoleAsync(f.Agency, f.ClientId, "Y", "", [agencyBillingId]))).Should().ThrowAsync<ArgumentException>();

        // A client member holding Roles:Manage but not Invoices:View can't put it on a role.
        var managerRole = await CustomRoleAsync(f, "Role Manager", "clientportal:Roles:Manage", "clientportal:Users:View");
        var manager = Guid.NewGuid();
        await f.Members.GrantAccessAsync(manager, f.ClientId, null, null, [managerRole]);
        await f.SaveAsync();
        var invoicesId = (await f.Uow.Context.Permissions.FirstAsync(p => p.Code == "clientportal:Invoices:View")).Id;
        var rolesManageId = (await f.Uow.Context.Permissions.FirstAsync(p => p.Code == "clientportal:Roles:Manage")).Id;

        await ((Func<Task>)(() => f.Roles.CreateCustomRoleAsync(ClientCaller.ClientMember(manager), f.ClientId, "Z", "", [invoicesId])))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't grant*");
        var ok = await f.Roles.CreateCustomRoleAsync(ClientCaller.ClientMember(manager), f.ClientId, "Z", "", [rolesManageId]);
        ok.Audience.Should().Be(RoleAudience.Client);
        ok.ClientEntityId.Should().Be(f.ClientId);
    }

    [Fact]
    public async Task CustomRole_BuiltInRolesAreFixed()
    {
        var f = await Fixture.CreateAsync();
        var builtIn = await f.RoleIdAsync(ClientRoles.Executive);

        await ((Func<Task>)(() => f.Roles.UpdateCustomRoleAsync(f.Agency, f.ClientId, builtIn, "Renamed", ""))).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => f.Roles.SetPermissionsAsync(f.Agency, f.ClientId, builtIn, []))).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => f.Roles.DeleteCustomRoleAsync(f.Agency, f.ClientId, builtIn))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CustomRole_EditingPermissionsNotifiesEveryHolder_AndDeleteIsBlockedWhileAssigned()
    {
        var f = await Fixture.CreateAsync();
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Admin);
        var roleId = await CustomRoleAsync(f, "Auditor", "clientportal:Time Logs:View");
        var holder = Guid.NewGuid();
        await f.Members.GrantAccessAsync(holder, f.ClientId, null, null, [roleId]);
        await f.SaveAsync();
        f.Publisher.Invocations.Clear();

        var exportId = (await f.Uow.Context.Permissions.FirstAsync(p => p.Code == "clientportal:Time Logs:Export")).Id;
        await f.Roles.SetPermissionsAsync(f.Agency, f.ClientId, roleId, [exportId]);
        await f.SaveAsync();

        (await f.Access.ResolveAsync(holder, f.ClientId))!.Permissions.Should().Equal("clientportal:Time Logs:Export");
        f.Publisher.Verify(p => p.Publish(
            It.Is<ClientMembershipChanged>(m => m.UserId == holder && m.ClientEntityId == f.ClientId),
            It.IsAny<CancellationToken>()), Times.Once);

        await ((Func<Task>)(() => f.Roles.DeleteCustomRoleAsync(f.Agency, f.ClientId, roleId))).Should().ThrowAsync<InvalidOperationException>();
        var holderMembership = await f.Uow.Context.Memberships.FirstAsync(m => m.UserId == holder);
        await f.Members.SetRolesAsync(f.Agency, f.ClientId, holderMembership.Id, [await f.RoleIdAsync(ClientRoles.Executive)]);
        await f.SaveAsync();
        await f.Roles.DeleteCustomRoleAsync(f.Agency, f.ClientId, roleId);
        await f.SaveAsync();
        (await f.Uow.Context.Roles.AnyAsync(r => r.Id == roleId)).Should().BeFalse();
    }

    // ── Authorizing an invitation (AuthApi asks before it sends one) ─────────────────────────

    private static async Task<Guid> StaffOwnerAsync(Fixture f)
    {
        var owner = Guid.NewGuid();
        await f.Staff.AddAsync(new UserMembership { TenantId = f.TenantId, UserId = owner }, [TenantRoles.Owner], CancellationToken.None);
        await f.SaveAsync();
        return owner;
    }

    [Fact]
    public async Task Invite_CompanyStaffAndClientAdmins_MayInvite_AndTheAnswerCarriesTheWording()
    {
        var f = await Fixture.CreateAsync();
        var owner = await StaffOwnerAsync(f);
        var clientAdmin = Guid.NewGuid();
        await f.GrantAsync(clientAdmin, f.ClientId, ClientRoles.Admin);
        var exec = new[] { await f.RoleIdAsync(ClientRoles.Executive) };

        var byStaff = await f.Members.AuthorizeInviteAsync(owner, f.ClientId, "new@x.com", exec);
        var byAdmin = await f.Members.AuthorizeInviteAsync(clientAdmin, f.ClientId, "new@x.com", exec);

        byStaff.Should().Be(byAdmin);
        byStaff.TenantId.Should().Be(f.TenantId);
        byStaff.TenantName.Should().Be("Agency");
        byStaff.ClientName.Should().Be("Client A");
    }

    [Fact]
    public async Task Invite_RefusesSomeoneWithoutUsersInvite_OrWithNoStandingAtAll()
    {
        var f = await Fixture.CreateAsync();
        var exec = Guid.NewGuid(); // Client Executive: no Users:Invite
        await f.GrantAsync(exec, f.ClientId, ClientRoles.Executive);
        var roles = new[] { await f.RoleIdAsync(ClientRoles.SiteSupervisor) };

        await ((Func<Task>)(() => f.Members.AuthorizeInviteAsync(exec, f.ClientId, "a@x.com", roles))).Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(() => f.Members.AuthorizeInviteAsync(Guid.NewGuid(), f.ClientId, "a@x.com", roles))).Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Invite_AdminOfAnotherClient_IsRefused()
    {
        var f = await Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        var adminOfB = Guid.NewGuid();
        await f.GrantAsync(adminOfB, clientB, ClientRoles.Admin);

        var act = () => f.Members.AuthorizeInviteAsync(adminOfB, f.ClientId, "a@x.com", [f.RoleIdAsync(ClientRoles.Executive).Result]);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Invite_CannotOfferARoleCarryingPermissionsTheInviterLacks()
    {
        var f = await Fixture.CreateAsync();
        var inviterRole = await CustomRoleAsync(f, "Inviter", "clientportal:Users:View", "clientportal:Users:Invite");
        var inviter = Guid.NewGuid();
        await f.Members.GrantAccessAsync(inviter, f.ClientId, null, null, [inviterRole]);
        await f.SaveAsync();

        await ((Func<Task>)(() => f.Members.AuthorizeInviteAsync(inviter, f.ClientId, "a@x.com", [f.RoleIdAsync(ClientRoles.FinanceManager).Result])))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*can't grant*");
        (await f.Members.AuthorizeInviteAsync(inviter, f.ClientId, "a@x.com", [inviterRole])).ClientName.Should().Be("Client A");
    }

    [Fact]
    public async Task Invite_RefusesSomeoneWhoAlreadyBelongs_NoRoles_AndAnInactiveClient()
    {
        var f = await Fixture.CreateAsync();
        var owner = await StaffOwnerAsync(f);
        var member = Guid.NewGuid();
        await f.GrantAsync(member, f.ClientId, ClientRoles.Executive);
        var exec = new[] { await f.RoleIdAsync(ClientRoles.Executive) };

        await ((Func<Task>)(() => f.Members.AuthorizeInviteAsync(owner, f.ClientId, $"  {member.ToString().ToUpperInvariant()}@X.com ", exec)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*already belongs*");
        await ((Func<Task>)(() => f.Members.AuthorizeInviteAsync(owner, f.ClientId, "new@x.com", []))).Should().ThrowAsync<ArgumentException>();

        await f.Entities.UpsertAsync(f.TenantId, f.ClientId, null, "Client A", active: false);
        await f.SaveAsync();
        await ((Func<Task>)(() => f.Members.AuthorizeInviteAsync(owner, f.ClientId, "new@x.com", exec)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*not active*");
    }

    // ── Keeping the two sides apart ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TheAgencysRoleEndpoints_CannotReachAClientsRoles()
    {
        var f = await Fixture.CreateAsync();
        var clientRole = await f.RoleIdAsync(ClientRoles.Executive);
        var custom = await CustomRoleAsync(f, "Auditor");
        var staffRoles = new RoleService(f.Uow);

        foreach (var id in new[] { clientRole, custom })
        {
            await ((Func<Task>)(() => staffRoles.SetPermissionsAsync(id, [], CancellationToken.None))).Should().ThrowAsync<KeyNotFoundException>();
            await ((Func<Task>)(() => staffRoles.UpdateCustomRoleAsync(id, "x", "y", CancellationToken.None))).Should().ThrowAsync<KeyNotFoundException>();
            await ((Func<Task>)(() => staffRoles.DeleteCustomRoleAsync(id, CancellationToken.None))).Should().ThrowAsync<KeyNotFoundException>();
            (await staffRoles.FindOneWithPermissionsAsync(id, CancellationToken.None)).Should().BeNull();
        }
    }

    // ── Mirroring the portal's clients ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertClientEntity_CreatesThenUpdates_ButNeverMovesACompany()
    {
        var f = await Fixture.CreateAsync();
        var id = Guid.NewGuid();

        await f.Entities.UpsertAsync(f.TenantId, id, "hris-7", "  Acme  ", true);
        await f.SaveAsync();
        var created = await f.Uow.Context.ClientEntities.SingleAsync(e => e.Id == id);
        created.Name.Should().Be("Acme");
        created.ExternalId.Should().Be("hris-7");
        created.Status.Should().Be("Active");

        await f.Entities.UpsertAsync(f.TenantId, id, null, "Acme Corp", false);
        await f.SaveAsync();
        var updated = await f.Uow.Context.ClientEntities.SingleAsync(e => e.Id == id);
        updated.Name.Should().Be("Acme Corp");
        updated.Status.Should().Be("Inactive");

        await ((Func<Task>)(() => f.Entities.UpsertAsync(Guid.NewGuid(), id, null, "Acme", true))).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => f.Entities.UpsertAsync(f.TenantId, Guid.NewGuid(), null, " ", true))).Should().ThrowAsync<ArgumentException>();
    }
}
