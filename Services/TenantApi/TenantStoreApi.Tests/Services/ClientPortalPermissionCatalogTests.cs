using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Core;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

public class ClientPortalPermissionCatalogTests
{
    private static PermissionCatalogSeederService CreateSut(out IUnitOfWorkService uow)
    {
        uow = TenantTestContextFactory.CreateUnitOfWork();
        return new PermissionCatalogSeederService(uow, [new HrmsPermissionCatalog(), new ClientPortalPermissionCatalog()]);
    }

    [Fact]
    public async Task Seeds_the_client_and_billing_permissions_with_the_product_prefix()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var permissions = await uow.Context.Permissions.Where(x => x.Product == "clientportal" && x.Module == "Client Portal").ToListAsync();
        permissions.Select(x => x.Code).Should().BeEquivalentTo(
        [
            "clientportal:Client:Manage",
            "clientportal:Billing:View",
            "clientportal:Billing:Manage",
            "clientportal:Billing:Approve",
            "clientportal:Billing:Issue",
            "clientportal:Operations:View",
            "clientportal:Operations:Manage",
        ]);
    }

    [Fact]
    public async Task Seeds_the_client_side_permissions_ported_from_the_portal_with_the_product_prefix()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var permissions = await uow.Context.Permissions.Where(x => x.Product == "clientportal" && x.Module == "Client Users").ToListAsync();
        permissions.Select(x => x.Code).Should().BeEquivalentTo(
        [
            "clientportal:Users:View", "clientportal:Users:Invite", "clientportal:Users:AssignRoles", "clientportal:Users:Manage",
            "clientportal:Roles:Manage",
            "clientportal:Time Logs:View", "clientportal:Time Logs:Review", "clientportal:Time Logs:SignOff", "clientportal:Time Logs:Export",
            "clientportal:Invoices:View", "clientportal:Invoices:Download",
            "clientportal:Payments:View", "clientportal:Payments:SubmitProof",
            "clientportal:Disputes:View", "clientportal:Disputes:Create",
            "clientportal:Reports:View",
            "clientportal:Manpower:Request",
            "clientportal:Document Vault:View",
            "clientportal:Ratings:Submit",
            "clientportal:Notices:View",
        ]);
        // The prefix is what keeps them from being read as HRMS codes.
        permissions.Should().OnlyContain(p => Onepunch.Common.Lib.Security.PermissionProducts.ProductOf(p.Code) == "clientportal");
    }

    [Fact]
    public async Task Seeds_the_built_in_client_roles_for_the_client_audience_with_the_portals_grants()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var roles = await uow.Context.Roles.Where(x => x.Audience == RoleAudience.Client).ToListAsync();
        roles.Select(r => r.Name).Should().BeEquivalentTo(["Client Admin", "Client Executive", "Finance Manager", "Site Supervisor"]);
        roles.Should().OnlyContain(r => r.IsSystemRole && r.TenantId == null && r.ClientEntityId == null && r.Product == "clientportal");

        async Task<List<string>> CodesOf(string name) => await uow.Context.RolePermissions
            .Where(x => x.Role.Name == name && x.Role.IsSystemRole)
            .Select(x => x.Permission.Code).ToListAsync();

        (await CodesOf("Client Admin")).Should().BeEquivalentTo(ClientPortalPermissionCatalog.ClientCodes);
        (await CodesOf("Finance Manager")).Should().BeEquivalentTo(
        [
            "clientportal:Invoices:View", "clientportal:Invoices:Download", "clientportal:Payments:View", "clientportal:Payments:SubmitProof",
            "clientportal:Disputes:View", "clientportal:Disputes:Create", "clientportal:Reports:View", "clientportal:Time Logs:View",
        ]);
        (await CodesOf("Site Supervisor")).Should().Contain("clientportal:Time Logs:SignOff").And.NotContain("clientportal:Users:View");
        (await CodesOf("Client Executive")).Should().Contain("clientportal:Users:View").And.NotContain("clientportal:Roles:Manage");
    }

    [Fact]
    public async Task Client_system_roles_are_not_offered_to_the_agency_and_stay_seeded_once()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);
        await sut.EnsureSeededAsync(CancellationToken.None);

        var agencyRoles = await new RoleService(uow).FindAllForTenantAsync(Guid.NewGuid(), CancellationToken.None);
        agencyRoles.Select(r => r.Name).Should().NotContain(["Client Admin", "Client Executive", "Finance Manager", "Site Supervisor"]);
        agencyRoles.Select(r => r.Name).Should().Contain(["Owner", "Admin", "Member", "Employee", "Client"]);
        (await uow.Context.Roles.CountAsync(x => x.Name == "Client Admin")).Should().Be(1);
    }

    [Fact]
    public async Task Owner_gets_it_but_Admin_does_not()
    {
        var sut = CreateSut(out var uow);

        await sut.EnsureSeededAsync(CancellationToken.None);

        var owner = await uow.Context.Roles.FirstAsync(x => x.Name == "Owner");
        var admin = await uow.Context.Roles.FirstAsync(x => x.Name == "Admin");
        (await uow.Context.RolePermissions.AnyAsync(x => x.RoleId == owner.Id && x.Permission.Code == "clientportal:Client:Manage")).Should().BeTrue();
        (await uow.Context.RolePermissions.AnyAsync(x => x.RoleId == admin.Id && x.Permission.Product == "clientportal")).Should().BeFalse();
    }
}
