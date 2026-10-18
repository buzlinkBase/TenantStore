using FluentAssertions;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Domain.Entities;
using Xunit;

namespace TenantStoreApi.Tests.Services;

/// <summary>The Users page's Client tab: every client user of the company across its clients, for the staff who run the portal.</summary>
public class CompanyClientMembersTests
{
    [Fact]
    public async Task Lists_a_row_per_person_per_client_with_their_roles_and_status()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var second = await f.NewClientAsync("Client B");
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await f.GrantAsync(alice, f.ClientId, ClientRoles.Executive);
        await f.GrantAsync(alice, second, ClientRoles.Admin);
        await f.GrantAsync(bob, second, ClientRoles.Executive);

        var rows = await f.Members.ListAllForCompanyAsync(f.TenantId);

        rows.Should().HaveCount(3);
        rows.Where(r => r.UserId == alice).Select(r => r.ClientName).Should().BeEquivalentTo("Client A", "Client B");
        rows.Single(r => r.UserId == bob).Should().Match<CompanyClientMemberView>(r =>
            r.ClientEntityId == second && r.Status == "Active" && r.OwnStatus == "Active" && r.Roles.Single().Name == ClientRoles.Executive);
    }

    [Fact]
    public async Task A_person_switched_off_at_one_client_shows_inactive_there_and_active_at_the_other()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var second = await f.NewClientAsync("Client B");
        var alice = Guid.NewGuid();
        var membership = await f.GrantAsync(alice, f.ClientId, ClientRoles.Executive);
        await f.GrantAsync(alice, second, ClientRoles.Executive);
        await f.Members.SetStatusAsync(f.Agency, f.ClientId, membership.Id, "Inactive");
        await f.SaveAsync();

        var rows = await f.Members.ListAllForCompanyAsync(f.TenantId);

        rows.Single(r => r.ClientEntityId == f.ClientId).Status.Should().Be("Inactive");
        rows.Single(r => r.ClientEntityId == second).Status.Should().Be("Active");
    }

    [Fact]
    public async Task Leaves_out_other_companies_client_users_and_company_staff()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var staff = Guid.NewGuid();
        await f.Staff.AddAsync(new UserMembership { TenantId = f.TenantId, UserId = staff }, [TenantRoles.Owner], CancellationToken.None);
        await f.SaveAsync();
        var other = await ClientAccessTests.Fixture.CreateAsync();
        await other.GrantAsync(Guid.NewGuid(), other.ClientId, ClientRoles.Executive);
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Executive);

        var rows = await f.Members.ListAllForCompanyAsync(f.TenantId);

        rows.Should().ContainSingle();
        rows.Should().NotContain(r => r.UserId == staff);
    }

    [Fact]
    public async Task Only_active_staff_holding_client_manage_may_ask()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        await f.Staff.AddAsync(new UserMembership { TenantId = f.TenantId, UserId = owner }, [TenantRoles.Owner], CancellationToken.None);
        await f.Staff.AddAsync(new UserMembership { TenantId = f.TenantId, UserId = member }, [TenantRoles.None], CancellationToken.None);
        await f.SaveAsync();

        await f.Access.AgencyStaffAsync(owner, f.TenantId);
        await ((Func<Task>)(() => f.Access.AgencyStaffAsync(member, f.TenantId))).Should().ThrowAsync<UnauthorizedAccessException>();
        await ((Func<Task>)(() => f.Access.AgencyStaffAsync(outsider, f.TenantId))).Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
