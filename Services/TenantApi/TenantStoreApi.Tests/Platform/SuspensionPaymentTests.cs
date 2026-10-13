using FluentAssertions;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Tests.TestSupport;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

/// <summary>
/// Paying lifts a suspension for non-payment, but never an operator's suspension -- that one is
/// lifted only by reinstating.
/// </summary>
public class SuspensionPaymentTests
{
    private const string Operator = "platform:ops@onepunch.test";

    private static PlatformAccountActionsService Actions(SubscriptionTestHost host) => new(
        host.Uow, host.Subscriptions, host.Events, host.Notifier, host.StateMachine,
        Microsoft.Extensions.Options.Options.Create(host.Options), host.Gateway, host.Clock);

    private static async Task<Guid> ActiveAccountAsync(SubscriptionTestHost host)
    {
        var (owner, _) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await host.SaveAsync();
        return owner;
    }

    private static RecordPaymentCommand Payment => new() { Amount = 1m, Method = PaymentMethod.BankTransfer };

    [Fact]
    public async Task SuspendedForNonPayment_OwesAReactivationPayment_AndPayingReactivates()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);
        var subscription = host.SubscriptionOf(owner);
        subscription.SubStatus = AccountSubscriptionStatus.Suspended;
        subscription.CurrentPeriodEndUtc = host.Clock.UtcNow.AddDays(-30);
        await host.SaveAsync();

        var due = await host.Subscriptions.GetPaymentDueAsync(subscription);
        due.Should().NotBeNull();
        due!.Reason.Should().Be(PaymentDueReason.Reactivation);

        await host.Subscriptions.RecordPaymentAsync(owner, Payment, "owner");
        await host.SaveAsync();

        host.SubscriptionOf(owner).SubStatus.Should().Be(AccountSubscriptionStatus.Active);
    }

    [Fact]
    public async Task OperatorSuspension_OwesNothing_AndAPaymentDoesNotLiftIt()
    {
        var host = new SubscriptionTestHost();
        var owner = await ActiveAccountAsync(host);
        await Actions(host).SuspendAsync(owner, "chargeback dispute", Operator);
        await host.SaveAsync();
        var subscription = host.SubscriptionOf(owner);

        (await host.Subscriptions.GetPaymentDueAsync(subscription)).Should().BeNull();

        var before = subscription.CurrentPeriodEndUtc;
        await host.Subscriptions.RecordPaymentAsync(owner, Payment, "platform");
        await host.SaveAsync();

        var after = host.SubscriptionOf(owner);
        after.SubStatus.Should().Be(AccountSubscriptionStatus.Suspended);
        after.SuspensionReason.Should().Be("chargeback dispute");
        after.CurrentPeriodEndUtc.Should().BeAfter(host.Clock.UtcNow, "the paid period is still recorded");
        before.Should().NotBeNull();
    }

    [Fact]
    public async Task OperatorSuspension_StaysBlockedForTheOwner_UntilReinstated()
    {
        var host = new SubscriptionTestHost();
        var (owner, tenantId) = await host.TrialAccountAsync();
        await host.Subscriptions.SetPlanAsync(owner, new SetPlanCommand { PlanCode = "payroll", PeriodEndUtc = host.Clock.UtcNow.AddMonths(1) }, "platform");
        await Actions(host).SuspendAsync(owner, "abuse report", Operator);
        await host.SaveAsync();

        (await host.Resolver.ResolveForTenantAsync(tenantId)).IsBlocked.Should().BeTrue();

        await Actions(host).ReinstateAsync(owner, "cleared", Operator);
        await host.SaveAsync();

        (await host.Resolver.ResolveForTenantAsync(tenantId)).IsBlocked.Should().BeFalse();
    }
}
