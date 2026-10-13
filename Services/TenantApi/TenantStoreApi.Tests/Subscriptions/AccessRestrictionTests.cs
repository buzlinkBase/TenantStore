using FluentAssertions;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using Xunit;

namespace TenantStoreApi.Tests.Subscriptions;

/// <summary>
/// An operator's suspension or legal hold stops the whole account -- the owner too -- and tells only
/// the owner and billing admins why. A lapse for non-payment is not such a block.
/// </summary>
public class AccessRestrictionTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly SubscriptionStateMachine _machine = new();

    private EntitlementSnapshot Build(AccountSubscription subscription) =>
        EntitlementSnapshotBuilder.Build(subscription, null, Guid.NewGuid(), Now, _machine);

    [Fact]
    public void OperatorSuspension_RestrictsTheAccount_WithItsReason()
    {
        var snapshot = Build(new AccountSubscription { SubStatus = AccountSubscriptionStatus.Suspended, SuspensionReason = "Chargeback dispute" });

        snapshot.Restriction.Should().Be(new AccessRestriction(AccessRestrictionKinds.Suspended, "Chargeback dispute"));
        snapshot.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void SuspensionForNonPayment_IsNotARestriction()
    {
        var snapshot = Build(new AccountSubscription { SubStatus = AccountSubscriptionStatus.Suspended });

        snapshot.Restriction.Should().BeNull();
        snapshot.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void AStaleSuspensionReason_IsIgnored_OnceTheAccountIsActiveAgain()
    {
        var snapshot = Build(new AccountSubscription { SubStatus = AccountSubscriptionStatus.Active, SuspensionReason = "old" });

        snapshot.Restriction.Should().BeNull();
    }

    [Fact]
    public void LegalHold_RestrictsAnActiveAccount()
    {
        var snapshot = Build(new AccountSubscription
        {
            SubStatus = AccountSubscriptionStatus.Active,
            LegalHold = true,
            LegalHoldReason = "Labor case",
        });

        snapshot.Restriction.Should().Be(new AccessRestriction(AccessRestrictionKinds.LegalHold, "Labor case"));
    }

    [Fact]
    public void Suspension_OutranksLegalHold()
    {
        var snapshot = Build(new AccountSubscription
        {
            SubStatus = AccountSubscriptionStatus.Suspended,
            SuspensionReason = "Abuse report",
            LegalHold = true,
            LegalHoldReason = "Labor case",
        });

        snapshot.Restriction!.Kind.Should().Be(AccessRestrictionKinds.Suspended);
    }

    [Theory]
    [InlineData(true, "GET")]
    [InlineData(true, "POST")]
    [InlineData(false, "GET")]
    public void ABlockedAccount_IsLockedToEveryone_OwnerIncluded(bool billingUser, string method)
    {
        var snapshot = new EntitlementSnapshot
        {
            AccessMode = SubscriptionAccessMode.Full,
            Restriction = new AccessRestriction(AccessRestrictionKinds.LegalHold, "Audit"),
        };

        SubscriptionAccessPolicy.Decide(snapshot, method, billingUser).Should().Be(SubscriptionAccessDecision.Locked);
    }

    [Fact]
    public void OnlyBillingUsers_SeeTheReason()
    {
        var restriction = new AccessRestriction(AccessRestrictionKinds.Suspended, "Chargeback dispute");

        restriction.ForViewer(isBillingUser: true).Reason.Should().Be("Chargeback dispute");
        restriction.ForViewer(isBillingUser: false).Reason.Should().BeNull();
        restriction.ForViewer(isBillingUser: false).Kind.Should().Be(AccessRestrictionKinds.Suspended);
    }

    [Fact]
    public void TheLifecycle_ClearsAnOperatorReason_WhenItSuspendsForNonPayment()
    {
        var subscription = new AccountSubscription
        {
            SubStatus = AccountSubscriptionStatus.PastDue,
            GraceEndsAtUtc = Now.AddDays(-1),
            SuspensionReason = "stale",
        };

        _machine.Advance(subscription, Now, new SubscriptionOptions());

        subscription.SubStatus.Should().Be(AccountSubscriptionStatus.Suspended);
        subscription.SuspensionReason.Should().BeNull();
    }
}
