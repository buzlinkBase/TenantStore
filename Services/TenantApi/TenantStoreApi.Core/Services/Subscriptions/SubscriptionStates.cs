using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>A status change the lifecycle made, recorded as a billing event of <see cref="EventType"/>.</summary>
public sealed record SubscriptionTransition(
    AccountSubscriptionStatus From,
    AccountSubscriptionStatus To,
    string EventType);

/// <summary>
/// State pattern: one class per <see cref="AccountSubscriptionStatus"/>, each owning what access
/// that status grants and when (by date alone) it moves on. Paying, changing plan and canceling
/// are commands on <c>AccountSubscriptionService</c>; only the passage of time is handled here.
/// </summary>
public interface ISubscriptionState
{
    AccountSubscriptionStatus Status { get; }
    SubscriptionAccessMode AccessMode { get; }

    /// <summary>Applies the transition that's due at <paramref name="nowUtc"/>, if any.</summary>
    SubscriptionTransition? Advance(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options);
}

public static class BillingEventTypes
{
    public const string TrialStarted = "trial.started";
    public const string TrialExpired = "trial.expired";
    public const string TrialExtended = "trial.extended";
    public const string TrialRepeatDomainFlagged = "trial.repeat-domain-flagged";
    public const string LegacyBackfill = "subscription.legacy-backfill";
    public const string PastDue = "subscription.past-due";
    public const string Suspended = "subscription.suspended";
    public const string Canceled = "subscription.canceled";
    public const string CancelRequested = "subscription.cancel-requested";
    public const string Resumed = "subscription.resumed";
    public const string PlanChangeRequested = "plan.change-requested";
    public const string PlanChangeWithdrawn = "plan.change-withdrawn";
    public const string PlanChanged = "plan.changed";
    public const string PlanSetByAdmin = "plan.set-by-admin";
    public const string PaymentRecorded = "payment.recorded";
    public const string OverrideAdded = "override.added";
    public const string OverrideRemoved = "override.removed";
    public const string DiscountAdded = "discount.added";
    public const string DiscountRemoved = "discount.removed";
    public const string EmailSent = "email.sent";
}

public sealed class TrialingState : ISubscriptionState
{
    public AccountSubscriptionStatus Status => AccountSubscriptionStatus.Trialing;
    public SubscriptionAccessMode AccessMode => SubscriptionAccessMode.Full;

    public SubscriptionTransition? Advance(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options)
    {
        if (subscription.TrialEndsAtUtc is not { } trialEnds || nowUtc < trialEnds) return null;

        subscription.SubStatus = AccountSubscriptionStatus.Expired;
        return new(Status, AccountSubscriptionStatus.Expired, BillingEventTypes.TrialExpired);
    }
}

/// <summary>
/// Paid and current. At the end of the period: a scheduled downgrade switches plan; a requested
/// cancellation ends the subscription; otherwise, unpaid, it falls past due with a grace period.
/// </summary>
public sealed class ActiveState : ISubscriptionState
{
    public AccountSubscriptionStatus Status => AccountSubscriptionStatus.Active;
    public SubscriptionAccessMode AccessMode => SubscriptionAccessMode.Full;

    public SubscriptionTransition? Advance(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options)
    {
        if (subscription.PendingChangeTiming == PlanChangeTiming.AtPeriodEnd
            && subscription.PendingPlanId is { } pendingPlanId
            && subscription.PendingChangeEffectiveAtUtc is { } effectiveAt
            && nowUtc >= effectiveAt)
        {
            subscription.PlanId = pendingPlanId;
            subscription.BillingCycle = subscription.PendingBillingCycle ?? subscription.BillingCycle;
            // The new plan's own price applies from here on.
            subscription.LockedMonthlyPrice = null;
            subscription.LockedAnnualPrice = null;
            ClearPendingChange(subscription);
            return new(Status, Status, BillingEventTypes.PlanChanged);
        }

        if (subscription.CurrentPeriodEndUtc is not { } periodEnd || nowUtc < periodEnd) return null;

        if (subscription.CancelAtPeriodEnd)
        {
            subscription.SubStatus = AccountSubscriptionStatus.Canceled;
            subscription.CanceledAtUtc = periodEnd;
            subscription.PurgeAfterUtc = periodEnd.AddDays(options.PurgeAfterDays);
            return new(Status, AccountSubscriptionStatus.Canceled, BillingEventTypes.Canceled);
        }

        subscription.SubStatus = AccountSubscriptionStatus.PastDue;
        subscription.GraceEndsAtUtc = periodEnd.AddDays(options.GraceDays);
        return new(Status, AccountSubscriptionStatus.PastDue, BillingEventTypes.PastDue);
    }

    internal static void ClearPendingChange(AccountSubscription subscription)
    {
        subscription.PendingPlanId = null;
        subscription.PendingBillingCycle = null;
        subscription.PendingChangeTiming = null;
        subscription.PendingChangeEffectiveAtUtc = null;
    }
}

/// <summary>Period ended unpaid: full access while payment is chased, until the grace period ends.</summary>
public sealed class PastDueState : ISubscriptionState
{
    public AccountSubscriptionStatus Status => AccountSubscriptionStatus.PastDue;
    public SubscriptionAccessMode AccessMode => SubscriptionAccessMode.Full;

    public SubscriptionTransition? Advance(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options)
    {
        if (subscription.GraceEndsAtUtc is not { } graceEnds || nowUtc < graceEnds) return null;

        subscription.SubStatus = AccountSubscriptionStatus.Suspended;
        return new(Status, AccountSubscriptionStatus.Suspended, BillingEventTypes.Suspended);
    }
}

/// <summary>Grace period ended unpaid. Read-only until paid; nothing is deleted.</summary>
public sealed class SuspendedState : ISubscriptionState
{
    public AccountSubscriptionStatus Status => AccountSubscriptionStatus.Suspended;
    public SubscriptionAccessMode AccessMode => SubscriptionAccessMode.ReadOnly;

    public SubscriptionTransition? Advance(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options) => null;
}

/// <summary>Trial ended without payment. Read-only until paid; nothing is deleted.</summary>
public sealed class ExpiredState : ISubscriptionState
{
    public AccountSubscriptionStatus Status => AccountSubscriptionStatus.Expired;
    public SubscriptionAccessMode AccessMode => SubscriptionAccessMode.ReadOnly;

    public SubscriptionTransition? Advance(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options) => null;
}

/// <summary>
/// Canceled and past the paid period. Read-only; data is kept until PurgeAfterUtc (the purge
/// itself is phase 2). Paying again reactivates it.
/// </summary>
public sealed class CanceledState : ISubscriptionState
{
    public AccountSubscriptionStatus Status => AccountSubscriptionStatus.Canceled;
    public SubscriptionAccessMode AccessMode => SubscriptionAccessMode.ReadOnly;

    public SubscriptionTransition? Advance(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options) => null;
}

/// <summary>Picks the state object for a subscription's status and runs its time-based transitions.</summary>
public sealed class SubscriptionStateMachine
{
    // A run that was missed for days can owe several transitions at once (Active -> PastDue ->
    // Suspended); each state only knows its own next step, so step until none is due.
    private const int MaxStepsPerRun = 5;

    private readonly Dictionary<AccountSubscriptionStatus, ISubscriptionState> _states;

    public SubscriptionStateMachine() : this(
    [
        new TrialingState(), new ActiveState(), new PastDueState(),
        new SuspendedState(), new ExpiredState(), new CanceledState(),
    ])
    {
    }

    public SubscriptionStateMachine(IEnumerable<ISubscriptionState> states)
    {
        _states = states.ToDictionary(s => s.Status);
    }

    public ISubscriptionState StateOf(AccountSubscriptionStatus status) => _states[status];

    public SubscriptionAccessMode AccessModeOf(AccountSubscription subscription) =>
        StateOf(subscription.SubStatus).AccessMode;

    public List<SubscriptionTransition> Advance(AccountSubscription subscription, DateTime nowUtc, SubscriptionOptions options)
    {
        var transitions = new List<SubscriptionTransition>();
        for (var step = 0; step < MaxStepsPerRun; step++)
        {
            var transition = StateOf(subscription.SubStatus).Advance(subscription, nowUtc, options);
            if (transition == null) break;
            transitions.Add(transition);
        }
        return transitions;
    }
}
