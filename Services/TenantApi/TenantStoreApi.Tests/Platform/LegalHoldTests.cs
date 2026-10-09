using FluentAssertions;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>An operator's legal hold: records are kept past the usual period for an audit, protest, refund claim or case.</summary>
public class LegalHoldTests
{
    private const string Operator = "platform:ops@onepunch.test";

    private static PlatformAccountActionsService Actions(SubscriptionTestHost host) => new(
        host.Uow, host.Subscriptions, host.Events, host.Notifier, host.StateMachine,
        Microsoft.Extensions.Options.Options.Create(host.Options), host.Clock);

    private static async Task<AccountSubscription> ExpiredAccountAsync(SubscriptionTestHost host)
    {
        var (owner, _) = await host.TrialAccountAsync();
        host.Clock.Advance(TimeSpan.FromDays(8));
        var subscription = host.SubscriptionOf(owner);
        await host.Lifecycle.ProcessAsync(subscription.Id, CancellationToken.None);
        return subscription;
    }

    [Fact]
    public async Task SetHold_RecordsTheReason_AndWhoDidIt()
    {
        var host = new SubscriptionTestHost();
        var subscription = await ExpiredAccountAsync(host);

        await Actions(host).SetLegalHoldAsync(subscription.OwnerUserId, " BIR audit 2026 ", Operator);
        await host.SaveAsync();

        subscription.LegalHold.Should().BeTrue();
        subscription.LegalHoldReason.Should().Be("BIR audit 2026");
        var recorded = host.Uow.Context.BillingEvents.Single(e => e.Type == BillingEventTypes.LegalHoldSet);
        recorded.Actor.Should().Be(Operator);
    }

    [Fact]
    public async Task SetHold_NeedsAReason()
    {
        var host = new SubscriptionTestHost();
        var subscription = await ExpiredAccountAsync(host);

        var act = () => Actions(host).SetLegalHoldAsync(subscription.OwnerUserId, "  ", Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task SetHold_Twice_IsRefused()
    {
        var host = new SubscriptionTestHost();
        var subscription = await ExpiredAccountAsync(host);
        await Actions(host).SetLegalHoldAsync(subscription.OwnerUserId, "protest", Operator);

        var act = () => Actions(host).SetLegalHoldAsync(subscription.OwnerUserId, "protest", Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task SetHold_IsRefused_OnceEverythingIsDestroyed()
    {
        var host = new SubscriptionTestHost();
        var subscription = await ExpiredAccountAsync(host);
        subscription.PurgeState = DataPurgeState.ArchiveDestroyed;

        var act = () => Actions(host).SetLegalHoldAsync(subscription.OwnerUserId, "too late", Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task Release_WhenThePurgeDatePassedDuringTheHold_GivesTheMinimumNotice()
    {
        var host = new SubscriptionTestHost();
        var subscription = await ExpiredAccountAsync(host);
        await Actions(host).SetLegalHoldAsync(subscription.OwnerUserId, "audit", Operator);
        host.Clock.UtcNow = subscription.PurgeAfterUtc!.Value.AddDays(200);
        subscription.PurgeState = DataPurgeState.InProgress;

        await Actions(host).ReleaseLegalHoldAsync(subscription.OwnerUserId, "audit closed", Operator);

        subscription.LegalHold.Should().BeFalse();
        subscription.LegalHoldReason.Should().BeNull();
        subscription.PurgeAfterUtc.Should().Be(host.Clock.UtcNow.AddDays(host.Options.MinPurgeNoticeDays));
        subscription.PurgeState.Should().Be(DataPurgeState.Scheduled);
    }

    [Fact]
    public async Task Release_LeavesALaterPurgeDateAlone()
    {
        var host = new SubscriptionTestHost();
        var subscription = await ExpiredAccountAsync(host);
        await Actions(host).SetLegalHoldAsync(subscription.OwnerUserId, "audit", Operator);
        var purge = subscription.PurgeAfterUtc;

        await Actions(host).ReleaseLegalHoldAsync(subscription.OwnerUserId, "audit closed", Operator);

        subscription.PurgeAfterUtc.Should().Be(purge);
    }

    [Fact]
    public async Task Release_WhenNotOnHold_IsRefused()
    {
        var host = new SubscriptionTestHost();
        var subscription = await ExpiredAccountAsync(host);

        var act = () => Actions(host).ReleaseLegalHoldAsync(subscription.OwnerUserId, "n/a", Operator);

        await act.Should().ThrowAsync<SubscriptionRuleException>();
    }

    [Fact]
    public async Task Release_OfAnArchiveWhoseRetentionRanOutDuringTheHold_GivesNoticeBeforeDestroying()
    {
        var host = new SubscriptionTestHost();
        var subscription = await ExpiredAccountAsync(host);
        subscription.PurgeState = DataPurgeState.Purged;
        subscription.ArchiveRetainUntilUtc = host.Clock.UtcNow.AddDays(-30);
        subscription.LegalHold = true;

        await Actions(host).ReleaseLegalHoldAsync(subscription.OwnerUserId, "audit closed", Operator);

        subscription.ArchiveRetainUntilUtc.Should().Be(host.Clock.UtcNow.AddDays(host.Options.MinPurgeNoticeDays));
    }
}
