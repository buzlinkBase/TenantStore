using FluentAssertions;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>
/// What a company may do follows its subscription's dates the moment they pass -- or are edited --
/// without waiting for the hourly lifecycle job (which still makes it permanent).
/// </summary>
public class StatusFromDatesTests
{
    [Fact]
    public async Task ActiveRow_WithAPeriodLongOver_ReadsAsSuspended_WithoutTheJob()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        var row = host.SubscriptionOf(owner);
        row.SubStatus = AccountSubscriptionStatus.Active;
        row.CurrentPeriodEndUtc = host.Clock.UtcNow.AddDays(-30);
        await host.SaveAsync();

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);

        snapshot.Status.Should().Be(nameof(AccountSubscriptionStatus.Suspended));
        snapshot.IsReadOnly.Should().BeTrue();
        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active, "reads never save");
    }

    [Fact]
    public async Task ActiveRow_JustPastItsPeriod_ReadsAsPastDue_WithFullAccess()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        var row = host.SubscriptionOf(owner);
        row.SubStatus = AccountSubscriptionStatus.Active;
        row.CurrentPeriodEndUtc = host.Clock.UtcNow.AddDays(-2);
        await host.SaveAsync();

        var snapshot = await host.Resolver.ResolveForTenantAsync(tenantId);

        snapshot.Status.Should().Be(nameof(AccountSubscriptionStatus.PastDue));
        snapshot.IsReadOnly.Should().BeFalse();
        snapshot.GraceEndsAtUtc.Should().Be(row.CurrentPeriodEndUtc!.Value.AddDays(7));
    }

    [Fact]
    public async Task TrialPastItsEnd_ReadsAsExpired()
    {
        var host = new SubscriptionTestHost();
        var (_, tenantId) = await host.TrialAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(8));

        (await host.Resolver.ResolveForTenantAsync(tenantId)).Status.Should().Be(nameof(AccountSubscriptionStatus.Expired));
    }

    [Fact]
    public async Task ActiveRow_InItsPeriod_StaysActive()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddDays(10) }, "platform");
        await host.SaveAsync();

        (await host.Resolver.ResolveForTenantAsync(tenantId)).Status.Should().Be(nameof(AccountSubscriptionStatus.Active));
    }

    // A row put into PastDue by hand has no grace end -- it used to stay PastDue forever.
    [Fact]
    public void PastDue_WithNoGraceDate_GetsOne_AndMovesOn()
    {
        var periodEnd = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var row = new AccountSubscription { SubStatus = AccountSubscriptionStatus.PastDue, CurrentPeriodEndUtc = periodEnd };

        new SubscriptionStateMachine().Advance(row, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), new SubscriptionOptions());

        row.GraceEndsAtUtc.Should().Be(periodEnd.AddDays(7));
        row.SubStatus.Should().Be(AccountSubscriptionStatus.Suspended);
    }

    [Fact]
    public void PastDue_WithNoGraceDate_AndARecentPeriodEnd_StaysPastDue()
    {
        var now = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
        var row = new AccountSubscription { SubStatus = AccountSubscriptionStatus.PastDue, CurrentPeriodEndUtc = now.AddDays(-2) };

        new SubscriptionStateMachine().Advance(row, now, new SubscriptionOptions());

        row.SubStatus.Should().Be(AccountSubscriptionStatus.PastDue);
        row.GraceEndsAtUtc.Should().Be(now.AddDays(5));
    }
}

/// <summary>
/// Companies that finished setting up but were never marked activated -- existing ones, or a
/// missed provisioning message -- are caught up from AuthApi's record, then backfilled.
/// </summary>
public class CompanyActivationSyncTests
{
    private sealed class FakeSource(Dictionary<Guid, DateTime> ready) : IProvisionedTenantSource
    {
        public Task<IReadOnlyDictionary<Guid, DateTime>> GetReadyAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token) =>
            Task.FromResult<IReadOnlyDictionary<Guid, DateTime>>(
                ready.Where(kv => tenantIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private static CompanyActivationSyncService Sync(SubscriptionTestHost host, Dictionary<Guid, DateTime> ready) =>
        new(host.Uow, new FakeSource(ready), host.Subscriptions, Options.Create(host.Options), host.Clock);

    private static SubscriptionBackfillService Backfill(SubscriptionTestHost host, Dictionary<Guid, DateTime> ready) =>
        new(host.Uow, host.Events, Options.Create(host.Options), host.Clock, Sync(host, ready));

    [Fact]
    public async Task ActivatesOnlyCompaniesThatFinishedSettingUp_AtTheTimeTheyDid()
    {
        var host = new SubscriptionTestHost();
        var finished = host.AddCompany(Guid.NewGuid(), "Finished", activated: false);
        var notYet = host.AddCompany(Guid.NewGuid(), "Not yet", activated: false);
        var readyAt = host.Clock.UtcNow.AddDays(-200);

        var count = await Sync(host, new() { [finished] = readyAt }).SyncAsync(CancellationToken.None);

        count.Should().Be(1);
        host.Uow.Context.Tenants.Single(t => t.Id == finished).ActivatedAtUtc.Should().Be(readyAt);
        host.Uow.Context.Tenants.Single(t => t.Id == notYet).ActivatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ExistingCustomer_GetsTheLegacyPlan_NotATrial()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        var company = host.AddCompany(owner, activated: false);

        (await Backfill(host, new() { [company] = host.Clock.UtcNow.AddDays(-200) }).EnsureBackfilledAsync(CancellationToken.None))
            .Should().Be(1);

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active);
    }

    [Fact]
    public async Task RecentSignup_WhoseMessageWentMissing_StartsATrial()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        var company = host.AddCompany(owner, activated: false);

        await Backfill(host, new() { [company] = host.Clock.UtcNow.AddDays(-1) }).EnsureBackfilledAsync(CancellationToken.None);

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Trialing);
    }

    [Fact]
    public async Task AnOwnerWithAPlan_GetsNoSecondOne()
    {
        var host = new SubscriptionTestHost();
        var (owner, _) = await host.TrialAccountAsync();
        var second = host.AddCompany(owner, "Second", activated: false);

        await Backfill(host, new() { [second] = host.Clock.UtcNow }).EnsureBackfilledAsync(CancellationToken.None);

        host.Uow.Context.AccountSubscriptions.Should().ContainSingle();
    }

    [Fact]
    public async Task WhenAuthApiCantAnswer_NothingIsActivated_OrBilled()
    {
        var host = new SubscriptionTestHost();
        var owner = Guid.NewGuid();
        host.AddCompany(owner, activated: false);
        host.AddMember(host.AddCompany(Guid.NewGuid(), "Other", activated: false), "Employee", userId: Guid.NewGuid());

        (await Backfill(host, new()).EnsureBackfilledAsync(CancellationToken.None)).Should().Be(0);

        host.Uow.Context.AccountSubscriptions.Should().BeEmpty();
    }
}
