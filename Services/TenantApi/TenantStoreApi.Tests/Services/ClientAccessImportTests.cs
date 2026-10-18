using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Controllers;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using Xunit;

namespace TenantStoreApi.Tests.Services;

/// <summary>
/// The one-time move of a client's existing people and custom roles over from the client portal's own tables: additive, safe to run again,
/// and never silent about anything it could not bring over.
/// </summary>
public class ClientAccessImportTests
{
    private static ImportMember Member(Guid account, string email, bool active = true, params string[] roles) =>
        new(account, email, "Person", active, roles);

    [Fact]
    public async Task Brings_over_custom_roles_with_their_permissions_and_people_with_their_roles()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var admin = Guid.NewGuid();
        var auditor = Guid.NewGuid();

        var report = await f.Import.ImportAsync(f.ClientId, new ClientAccessImport(
            [new ImportRole("Night Auditor", "Reviews nights", ["clientportal:Time Logs:View", "clientportal:Time Logs:Export"])],
            [Member(admin, "a@x.com", true, "Client Admin"), Member(auditor, "n@x.com", true, "Night Auditor", "Finance Manager")]));
        await f.SaveAsync();

        report.Should().BeEquivalentTo(new { RolesCreated = 1, RolesAlreadyThere = 0, MembersGranted = 2 }, o => o.ExcludingMissingMembers());
        report.Warnings.Should().BeEmpty();
        (await f.Access.ResolveAsync(admin, f.ClientId))!.Roles.Should().Equal("Client Admin");
        var access = (await f.Access.ResolveAsync(auditor, f.ClientId))!;
        access.Roles.Should().BeEquivalentTo("Night Auditor", "Finance Manager");
        access.Permissions.Should().Contain(["clientportal:Time Logs:Export", "clientportal:Invoices:View"]);
        var role = await f.Uow.Context.Roles.SingleAsync(r => r.Name == "Night Auditor");
        (role.ClientEntityId, role.TenantId, role.Audience, role.IsSystemRole).Should().Be((f.ClientId, f.TenantId, RoleAudience.Client, false));
    }

    [Fact]
    public async Task Running_it_again_changes_nothing()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var user = Guid.NewGuid();
        var import = new ClientAccessImport([new ImportRole("Auditor", null, ["clientportal:Time Logs:View"])], [Member(user, "u@x.com", true, "Auditor")]);
        await f.Import.ImportAsync(f.ClientId, import);
        await f.SaveAsync();

        var second = await f.Import.ImportAsync(f.ClientId, import);
        await f.SaveAsync();

        second.RolesCreated.Should().Be(0);
        second.RolesAlreadyThere.Should().Be(1);
        (await f.Uow.Context.Roles.CountAsync(r => r.Name == "Auditor")).Should().Be(1);
        (await f.Uow.Context.Memberships.CountAsync(m => m.UserId == user)).Should().Be(1);
        (await f.Uow.Context.ClientMembershipRoles.CountAsync(l => l.UserMembership.UserId == user)).Should().Be(1);
    }

    [Fact]
    public async Task Only_adds_a_person_already_holding_other_roles_keeps_them()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var user = Guid.NewGuid();
        await f.GrantAsync(user, f.ClientId, ClientRoles.Executive);

        await f.Import.ImportAsync(f.ClientId, new ClientAccessImport([], [Member(user, "u@x.com", true, "Finance Manager")]));
        await f.SaveAsync();

        (await f.Access.ResolveAsync(user, f.ClientId))!.Roles.Should().BeEquivalentTo("Client Executive", "Finance Manager");
    }

    [Fact]
    public async Task Inactive_people_come_over_switched_off()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var off = Guid.NewGuid();

        var report = await f.Import.ImportAsync(f.ClientId, new ClientAccessImport([],
            [Member(Guid.NewGuid(), "a@x.com", true, "Client Admin"), Member(off, "off@x.com", false, "Client Executive")]));
        await f.SaveAsync();

        report.Warnings.Should().BeEmpty();
        var access = (await f.Access.ResolveAsync(off, f.ClientId))!;
        access.Status.Should().Be("Inactive");
        access.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_last_inactive_admin_is_left_active_and_the_warning_says_why()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var only = Guid.NewGuid();

        var report = await f.Import.ImportAsync(f.ClientId, new ClientAccessImport([], [Member(only, "only@x.com", false, "Client Admin")]));
        await f.SaveAsync();

        report.Warnings.Should().ContainSingle().Which.Should().Contain("left active").And.Contain("Client Admin");
        (await f.Access.ResolveAsync(only, f.ClientId))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Whatever_cannot_be_brought_over_is_reported_not_dropped_silently()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var ok = Guid.NewGuid();

        var report = await f.Import.ImportAsync(f.ClientId, new ClientAccessImport(
            [
                new ImportRole("Mixed", null, ["clientportal:Time Logs:View", "clientportal:Billing:Approve", "made:up:code"]),
                new ImportRole("Finance Manager", null, []),
            ],
            [
                Member(ok, "ok@x.com", true, "Mixed", "No Such Role"),
                Member(Guid.NewGuid(), "none@x.com", true, "No Such Role"),
                Member(Guid.NewGuid(), "empty@x.com", true),
            ]));
        await f.SaveAsync();

        report.RolesCreated.Should().Be(1);
        report.RolesAlreadyThere.Should().Be(1);
        report.MembersGranted.Should().Be(1);
        report.Warnings.Should().Contain(w => w.Contains("clientportal:Billing:Approve") && w.Contains("Mixed"));
        report.Warnings.Should().Contain(w => w.Contains("made:up:code"));
        report.Warnings.Should().Contain(w => w.Contains("'Finance Manager'") && w.Contains("built-in"));
        report.Warnings.Should().Contain(w => w.Contains("ok@x.com") && w.Contains("No Such Role"));
        report.Warnings.Should().Contain(w => w.Contains("none@x.com") && w.Contains("no role"));
        report.Warnings.Should().Contain(w => w.Contains("empty@x.com") && w.Contains("no role"));
        // Only the codes a client role may carry made it onto the role.
        (await f.Access.ResolveAsync(ok, f.ClientId))!.Permissions.Should().Equal("clientportal:Time Logs:View");
    }

    [Fact]
    public async Task An_unknown_client_is_not_found()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();

        var act = () => f.Import.ImportAsync(Guid.NewGuid(), new ClientAccessImport([], []));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task The_endpoint_returns_the_report_and_maps_a_missing_client_to_404()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var controller = new InternalClientAccessController(f.Access, f.Entities, f.Import);

        var ok = await controller.Import(f.ClientId, new ClientAccessImport([], [Member(Guid.NewGuid(), "a@x.com", true, "Client Admin")]), CancellationToken.None);
        var missing = await controller.Import(Guid.NewGuid(), new ClientAccessImport([], []), CancellationToken.None);

        ok.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<ImportReport>().Which.MembersGranted.Should().Be(1);
        missing.Should().BeOfType<NotFoundObjectResult>();
    }
}
