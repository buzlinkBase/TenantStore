using FluentAssertions;
using Moq;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Services;

/// <summary>
/// A suspended or legal-held agency's clients can't use the Client Portal: the standing is read from the owner's
/// subscription, applied as a layer over each person's access, and pushed to the portal when it may have changed.
/// </summary>
public class AgencyStandingTests
{
    private sealed class FixedStanding(AgencyStanding standing) : IAgencyStandingProvider
    {
        public Task<IReadOnlyDictionary<Guid, AgencyStanding>> GetAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, AgencyStanding>>(tenantIds.Distinct().ToDictionary(id => id, _ => standing));
    }

    // ── Reading the standing ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Provider_IsGoodForAnActiveAccount_AndForACompanyWithNoSubscription()
    {
        var host = new SubscriptionTestHost();
        var (_, tenantId) = await host.TrialAccountAsync();
        var orphan = host.AddCompany(Guid.NewGuid(), "No Plan");
        await host.SaveAsync();

        var result = await new AgencyStandingProvider(host.Uow).GetAsync([tenantId, orphan, Guid.NewGuid()]);

        result.Values.Should().OnlyContain(s => s == AgencyStanding.Good);
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Provider_ReportsSuspended_WhetherByAnOperatorOrTheLifecycle()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).SubStatus = TenantStoreApi.Domain.Entities.Subs.AccountSubscriptionStatus.Suspended;
        await host.SaveAsync();

        var result = await new AgencyStandingProvider(host.Uow).GetAsync([tenantId]);

        result[tenantId].Should().Be(AgencyStanding.Suspended);
        result[tenantId].BlocksClients().Should().BeTrue();
    }

    [Fact]
    public async Task Provider_ReportsLegalHold_EvenOnAnOtherwiseWorkingAccount()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        host.SubscriptionOf(owner).LegalHold = true;
        await host.SaveAsync();

        var result = await new AgencyStandingProvider(host.Uow).GetAsync([tenantId]);

        result[tenantId].Should().Be(AgencyStanding.LegalHold);
        result[tenantId].BlocksClients().Should().BeTrue();
    }

    // ── Applying it to a person's access ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(AgencyStanding.Suspended)]
    [InlineData(AgencyStanding.LegalHold)]
    public async Task Resolve_BlocksAnActivePerson_WithNoPermissions_WhenTheAgencyIsHeld(AgencyStanding standing)
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var user = Guid.NewGuid();
        await f.GrantAsync(user, f.ClientId, ClientRoles.Executive);
        var access = new ClientAccessService(f.Uow, f.Staff, new FixedStanding(standing));

        var resolved = await access.ResolveAsync(user, f.ClientId);

        resolved.Should().NotBeNull();
        resolved!.IsActive.Should().BeFalse();
        resolved.Status.Should().Be(ClientAccess.StatusAgencySuspended);
        resolved.Permissions.Should().BeEmpty();
        resolved.Roles.Should().Equal(ClientRoles.Executive);
    }

    [Fact]
    public async Task List_BlocksEveryClientOfAHeldAgency()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var second = await f.NewClientAsync("Client B");
        var user = Guid.NewGuid();
        await f.GrantAsync(user, f.ClientId, ClientRoles.Executive);
        await f.GrantAsync(user, second, ClientRoles.Executive);
        var access = new ClientAccessService(f.Uow, f.Staff, new FixedStanding(AgencyStanding.Suspended));

        var list = await access.ListForUserAsync(user);

        list.Should().HaveCount(2).And.OnlyContain(a => a.Status == ClientAccess.StatusAgencySuspended && !a.IsActive);
    }

    [Fact]
    public async Task Resolve_RestoresAccessAsSoonAsTheAgencyIsInGoodStandingAgain()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var user = Guid.NewGuid();
        await f.GrantAsync(user, f.ClientId, ClientRoles.Executive);

        var resolved = await new ClientAccessService(f.Uow, f.Staff, new FixedStanding(AgencyStanding.Good)).ResolveAsync(user, f.ClientId);

        resolved!.IsActive.Should().BeTrue();
        resolved.Permissions.Should().NotBeEmpty();
    }

    [Fact]
    public void ApplyStanding_LeavesAnAlreadySwitchedOffPersonWithTheirOwnReason()
    {
        var tenant = Guid.NewGuid();
        var off = new ClientAccess(Guid.NewGuid(), "Client", tenant, "Agency", "Inactive", ["Executive"], []);

        var result = ClientAccessService.ApplyStanding(off, new Dictionary<Guid, AgencyStanding> { [tenant] = AgencyStanding.Suspended });

        result.Status.Should().Be("Inactive");
    }

    [Fact]
    public void ApplyStanding_IgnoresOtherAgencies()
    {
        var access = new ClientAccess(Guid.NewGuid(), "Client", Guid.NewGuid(), "Agency", ClientAccess.StatusActive, ["Executive"], ["clientportal:Users:View"]);

        var result = ClientAccessService.ApplyStanding(access, new Dictionary<Guid, AgencyStanding> { [Guid.NewGuid()] = AgencyStanding.Suspended });

        result.IsActive.Should().BeTrue();
    }

    // ── Telling the portal ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publisher_SendsOneStatusChangePerClientPerson_WhenTheStandingMayHaveMoved()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        var second = await f.NewClientAsync("Client B");
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await f.GrantAsync(alice, f.ClientId, ClientRoles.Executive);
        await f.GrantAsync(alice, second, ClientRoles.Executive);
        await f.GrantAsync(bob, f.ClientId, ClientRoles.Executive);
        f.Publisher.Invocations.Clear();

        await new ClientStandingPublisher(f.Uow, f.Publisher.Object).PublishAsync([f.TenantId], BillingEventTypes.SuspendedByAdmin);

        f.Publisher.Verify(p => p.Publish(It.Is<ClientMembershipChanged>(m => m.ChangeType == "StatusChanged" && m.TenantId == f.TenantId),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
        f.Publisher.Verify(p => p.Publish(It.Is<ClientMembershipChanged>(m => m.UserId == alice && m.ClientEntityId == second),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publisher_StaysQuietForChangesThatCannotMoveTheStanding()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Executive);
        f.Publisher.Invocations.Clear();

        await new ClientStandingPublisher(f.Uow, f.Publisher.Object).PublishAsync([f.TenantId], BillingEventTypes.StorageAddOnPurchased);

        f.Publisher.Verify(p => p.Publish(It.IsAny<ClientMembershipChanged>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publisher_OnlyTouchesThePeopleOfTheAgencyThatChanged()
    {
        var f = await ClientAccessTests.Fixture.CreateAsync();
        await f.GrantAsync(Guid.NewGuid(), f.ClientId, ClientRoles.Executive);
        f.Publisher.Invocations.Clear();

        await new ClientStandingPublisher(f.Uow, f.Publisher.Object).PublishAsync([Guid.NewGuid()], BillingEventTypes.SuspendedByAdmin);

        f.Publisher.Verify(p => p.Publish(It.IsAny<ClientMembershipChanged>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
