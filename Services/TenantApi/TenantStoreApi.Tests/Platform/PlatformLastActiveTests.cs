using FluentAssertions;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>The "last active" the platform console shows on accounts and companies.</summary>
public class PlatformLastActiveTests
{
    private sealed class FakeActivity(Dictionary<Guid, DateTime> known, bool fails = false) : ITenantActivitySource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<Guid, DateTime>> GetLastActiveAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token)
        {
            Calls++;
            if (fails) return Task.FromResult<IReadOnlyDictionary<Guid, DateTime>>(new Dictionary<Guid, DateTime>());
            return Task.FromResult<IReadOnlyDictionary<Guid, DateTime>>(
                known.Where(k => tenantIds.Contains(k.Key)).ToDictionary(k => k.Key, k => k.Value));
        }
    }

    private static PlatformDirectoryService Directory(SubscriptionTestHost host, ITenantActivitySource source) =>
        new(host.Uow, host.StateMachine, source);

    [Fact]
    public async Task Companies_CarryTheirOwnLastActive_AndNullWhenNeverSignedIn()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        var active = host.AddCompany(owner, "Active Co", "boss@acme.ph");
        host.AddCompany(owner, "Quiet Co", "boss@acme.ph");
        await host.SaveAsync();
        var at = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

        var page = await Directory(host, new FakeActivity(new() { [active] = at })).ListCompaniesAsync(new CompanyQuery());

        page.Items.Single(c => c.Name == "Active Co").LastActiveUtc.Should().Be(at);
        page.Items.Single(c => c.Name == "Quiet Co").LastActiveUtc.Should().BeNull();
    }

    [Fact]
    public async Task Account_TakesTheNewestSignInAcrossItsCompanies_WithOneLookupPerPage()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        var first = host.AddCompany(owner, "First", "boss@acme.ph");
        var second = host.AddCompany(owner, "Second", "boss@acme.ph");
        await host.SaveAsync();
        var older = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        var source = new FakeActivity(new() { [first] = older, [second] = newer });

        var page = await Directory(host, source).ListAccountsAsync(new AccountQuery());

        page.Items.Should().ContainSingle().Which.LastActiveUtc.Should().Be(newer);
        source.Calls.Should().Be(1);
    }

    [Fact]
    public async Task WhenAuthCannotAnswer_TheDirectoryStillWorks_AndLastActiveIsEmpty()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        host.AddCompany(owner, "Acme", "boss@acme.ph");
        await host.SaveAsync();
        var directory = Directory(host, new FakeActivity([], fails: true));

        (await directory.ListAccountsAsync(new AccountQuery())).Items.Should().ContainSingle().Which.LastActiveUtc.Should().BeNull();
        (await directory.ListCompaniesAsync(new CompanyQuery())).Items.Should().ContainSingle().Which.LastActiveUtc.Should().BeNull();
    }
}
