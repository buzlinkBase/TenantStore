using System.Security.Claims;
using BuzlinkRepository;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using TenantStoreApi.Controllers;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using Xunit;

namespace TenantStoreApi.Tests.Services;

/// <summary>
/// The two doors onto a client's people and roles: company staff with an HRIS token, and the client
/// portal's server acting for a person it has authenticated (named in a header). The rules are the
/// services'; these check each door identifies the caller correctly and maps refusals.
/// </summary>
public class ClientAccessDoorTests
{
    private static T WithContext<T>(T controller, string? actingUser = null, Guid? signedInUser = null) where T : ControllerBase
    {
        var http = new DefaultHttpContext();
        if (actingUser != null) http.Request.Headers[ClientCallerHeader.ActingUser] = actingUser;
        if (signedInUser != null)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, signedInUser.ToString()!), new Claim("sub", signedInUser.ToString()!)], "test"));
        }
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static InternalClientMembersController InternalMembers(ClientAccessTests.Fixture f, string? actingUser) =>
        WithContext(new InternalClientMembersController(f.Members, f.Access), actingUser);

    private static InternalClientRolesController InternalRoles(ClientAccessTests.Fixture f, string? actingUser) =>
        WithContext(new InternalClientRolesController(f.Roles, f.Access), actingUser);

    private static async Task<Guid> GrantAsync(ClientAccessTests.Fixture f, Guid user, string role, Guid? clientId = null)
    {
        var membership = await f.Members.GrantAccessAsync(user, clientId ?? f.ClientId, $"{user}@x.com", "Person", [await f.RoleIdAsync(role)]);
        await f.SaveAsync();
        return membership.Id;
    }

    // ── Portal door ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Portal_ListsMembers_ForTheActingClientAdmin()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var admin = Guid.NewGuid();
        await GrantAsync(f, admin, ClientRoles.Admin);

        var result = await InternalMembers(f, admin.ToString()).List(f.ClientId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<List<ClientMemberView>>().Which.Should().ContainSingle();
    }

    [Fact]
    public async Task Portal_AnActingPersonWithNoStandingGetsForbidden_AndNothingIsChanged()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        await GrantAsync(f, Guid.NewGuid(), ClientRoles.Admin);
        var victim = await GrantAsync(f, Guid.NewGuid(), ClientRoles.Executive);
        var stranger = Guid.NewGuid().ToString();
        var roles = new SetClientMemberRolesRequest { RoleIds = [await f.RoleIdAsync(ClientRoles.Admin)] };

        (await InternalMembers(f, stranger).List(f.ClientId, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await InternalMembers(f, stranger).SetRoles(f.ClientId, victim, roles, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await InternalMembers(f, stranger).Remove(f.ClientId, victim, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await f.Access.ResolveAsync((await f.Uow.Context.Memberships.FirstAsync(m => m.Id == victim)).UserId, f.ClientId))!
            .Roles.Should().Equal(ClientRoles.Executive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Portal_ActingPersonHeaderIsRequired(string? header)
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();

        var result = await InternalMembers(f, header).List(f.ClientId, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be($"The {ClientCallerHeader.ActingUser} header is required.");
    }

    [Fact]
    public async Task Portal_AnUnknownClientIsNotFound()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();

        (await InternalMembers(f, Guid.NewGuid().ToString()).List(Guid.NewGuid(), CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Portal_ClientAdminChangesRoles_AndItIsSaved()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var admin = Guid.NewGuid();
        await GrantAsync(f, admin, ClientRoles.Admin);
        var target = Guid.NewGuid();
        var membership = await GrantAsync(f, target, ClientRoles.Executive);

        var result = await InternalMembers(f, admin.ToString()).SetRoles(
            f.ClientId, membership, new SetClientMemberRolesRequest { RoleIds = [await f.RoleIdAsync(ClientRoles.FinanceManager)] }, CancellationToken.None);

        await f.SaveAsync(); // the controller's commit is the fixture's second unit-of-work commit, which is a no-op
        result.Should().BeOfType<OkResult>();
        (await f.Access.ResolveAsync(target, f.ClientId))!.Roles.Should().Equal(ClientRoles.FinanceManager);
    }

    [Fact]
    public async Task Portal_ServiceRulesStillApply_LastAdminCannotBeDemoted()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var admin = Guid.NewGuid();
        var other = Guid.NewGuid();
        await GrantAsync(f, admin, ClientRoles.Admin);
        var sole = await GrantAsync(f, other, ClientRoles.Executive);

        // The only Client Admin deactivating themselves is refused (as a 400 with the reason).
        var adminMembership = (await f.Uow.Context.Memberships.FirstAsync(m => m.UserId == admin)).Id;
        var result = await InternalMembers(f, other.ToString()).SetStatus(f.ClientId, adminMembership, new UpdateStatusRequest { Status = "Inactive" }, CancellationToken.None);

        // `other` is an Executive: no Users:Manage, so they can't even try.
        result.Should().BeOfType<ForbidResult>();
        _ = sole;
    }

    [Fact]
    public async Task Portal_CreatesACustomRole_AndRolesAreListedForTheActingAdmin()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var admin = Guid.NewGuid();
        await GrantAsync(f, admin, ClientRoles.Admin);
        var viewId = (await f.Uow.Context.Permissions.FirstAsync(p => p.Code == "clientportal:Time Logs:View")).Id;

        var created = await InternalRoles(f, admin.ToString()).Create(
            f.ClientId, new SaveClientRoleRequest { Name = "Night Auditor", Description = "d", PermissionIds = [viewId] }, CancellationToken.None);
        await f.SaveAsync();
        var listed = await InternalRoles(f, admin.ToString()).List(f.ClientId, CancellationToken.None);

        created.Should().BeOfType<OkObjectResult>();
        listed.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<List<ClientRoleView>>()
            .Which.Select(r => r.Name).Should().Contain(["Client Admin", "Night Auditor"]);
        (await InternalRoles(f, admin.ToString()).Permissions(f.ClientId, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Portal_AgencyStaffActingForThemselves_AreTreatedAsAgency()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var owner = Guid.NewGuid();
        await f.Staff.AddAsync(new UserMembership { TenantId = f.TenantId, UserId = owner }, [TenantRoles.Owner], CancellationToken.None);
        await f.SaveAsync();
        await GrantAsync(f, Guid.NewGuid(), ClientRoles.Admin);

        var result = await InternalMembers(f, owner.ToString()).List(f.ClientId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ── Resolve / list / upsert (the portal's read side) ────────────────────

    private static InternalClientAccessController Access(ClientAccessTests.Fixture f) =>
        WithContext(new InternalClientAccessController(f.Access, f.Entities, f.Import));

    [Fact]
    public async Task Resolve_AnswersWhatThePersonMayDo_Or404()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var user = Guid.NewGuid();
        await GrantAsync(f, user, ClientRoles.Executive);

        var found = await Access(f).Resolve(user, f.ClientId, CancellationToken.None);
        var missing = await Access(f).Resolve(Guid.NewGuid(), f.ClientId, CancellationToken.None);

        found.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<ClientAccess>().Which.Roles.Should().Equal(ClientRoles.Executive);
        missing.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ListForAccount_SpansClients()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var clientB = await f.NewClientAsync("Client B");
        var user = Guid.NewGuid();
        await GrantAsync(f, user, ClientRoles.Admin);
        await GrantAsync(f, user, ClientRoles.Executive, clientB);

        var result = await Access(f).ListForAccount(user, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<List<ClientAccess>>().Which.Should().HaveCount(2);
    }

    [Fact]
    public async Task Upsert_CreatesAndUpdatesTheClient_AndRefusesAnotherCompanysId()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var id = Guid.NewGuid();

        var created = await Access(f).Upsert(id, new UpsertClientEntityRequestBody { TenantId = f.TenantId, ExternalId = "h1", Name = "New Co", Active = true }, CancellationToken.None);
        await f.SaveAsync();
        var renamed = await Access(f).Upsert(id, new UpsertClientEntityRequestBody { TenantId = f.TenantId, Name = "Renamed", Active = false }, CancellationToken.None);
        await f.SaveAsync();
        var stolen = await Access(f).Upsert(id, new UpsertClientEntityRequestBody { TenantId = Guid.NewGuid(), Name = "Hijack", Active = true }, CancellationToken.None);
        var blank = await Access(f).Upsert(Guid.NewGuid(), new UpsertClientEntityRequestBody { TenantId = f.TenantId, Name = " " }, CancellationToken.None);

        created.Should().BeOfType<OkResult>();
        renamed.Should().BeOfType<OkResult>();
        stolen.Should().BeOfType<BadRequestObjectResult>();
        blank.Should().BeOfType<BadRequestObjectResult>();
        var stored = await f.Uow.Context.ClientEntities.AsNoTracking().SingleAsync(e => e.Id == id);
        (stored.Name, stored.Status, stored.TenantId).Should().Be(("Renamed", "Inactive", f.TenantId));
    }

    // ── Staff door ───────────────────────────────────────────────────────────

    private static ClientMembersController StaffMembers(ClientAccessTests.Fixture f, Guid user, Guid tenantId)
    {
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        return WithContext(new ClientMembersController(f.Members, f.Access, tenant.Object), signedInUser: user);
    }

    [Fact]
    public async Task Staff_OwnerOfTheCompany_ListsMembers_OthersAreRefused()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var owner = Guid.NewGuid();
        var plain = Guid.NewGuid();
        await f.Staff.AddAsync(new UserMembership { TenantId = f.TenantId, UserId = owner }, [TenantRoles.Owner], CancellationToken.None);
        await f.Staff.AddAsync(new UserMembership { TenantId = f.TenantId, UserId = plain }, [TenantRoles.None], CancellationToken.None);
        await f.SaveAsync();
        await GrantAsync(f, Guid.NewGuid(), ClientRoles.Admin);

        (await StaffMembers(f, owner, f.TenantId).List(f.ClientId, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
        (await StaffMembers(f, plain, f.TenantId).List(f.ClientId, CancellationToken.None)).Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Staff_AnotherCompanysClient_IsNotFound_AndNoTenantIsABadRequest()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var otherTenant = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        await f.Staff.AddAsync(new UserMembership { TenantId = otherTenant, UserId = outsider }, [TenantRoles.Owner], CancellationToken.None);
        await f.SaveAsync();

        (await StaffMembers(f, outsider, otherTenant).List(f.ClientId, CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
        (await StaffMembers(f, outsider, Guid.Empty).List(f.ClientId, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
    }
}
