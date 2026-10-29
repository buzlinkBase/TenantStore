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
    public const string PlanOffered = "plan.offered";
    public const string PlanOfferRevoked = "plan.offer-revoked";
    public const string PaymentRecorded = "payment.recorded";
    public const string OverrideAdded = "override.added";
    public const string OverrideRemoved = "override.removed";
    public const string DiscountAdded = "discount.added";
    public const string DiscountRemoved = "discount.removed";
    public const string EmailSent = "email.sent";

    // Storage add-ons (StorageAddOnService, StorageOfferingCatalogService).
    public const string StorageOfferingCreated = "storage.offering-created";
    public const string StorageOfferingUpdated = "storage.offering-updated";
    public const string StorageOffered = "storage.offered";
    public const string StorageOfferRevoked = "storage.offer-revoked";
    public const string StorageAddOnPurchased = "storage.addon-purchased";
    public const string StorageAddOnGranted = "storage.addon-granted";
    public const string StorageAddOnRemoved = "storage.addon-removed";
    public const string StorageReductionRequested = "storage.reduction-requested";
    public const string StorageReductionWithdrawn = "storage.reduction-withdrawn";
    public const string StorageReductionApplied = "storage.reduction-applied";
    public const string StorageThresholdCrossed = "storage.threshold-crossed";
    /// <summary>An operator deleted a company's stored files for the owner (StorageFileCleanupService).</summary>
    public const string StorageFilesDeleted = "storage.files-deleted";

    // Data removal (DataPurgeService). A year after the lock, the company's data is archived and
    // deleted from the live databases; five years after its last entry the archive is destroyed.
    public const string DataPurgeRequested = "data.purge-requested";
    public const string DataPurged = "data.purged";
    public const string DataPurgeFailed = "data.purge-failed";
    public const string ArchiveDestroyRequested = "data.archive-destroy-requested";
    public const string ArchiveDestroyed = "data.archive-destroyed";
    public const string LegalHoldSet = "data.legal-hold-set";
    public const string LegalHoldReleased = "data.legal-hold-released";

    // Platform operator actions (PlatformAccountActionsService).
    public const string SuspendedByAdmin = "subscription.suspended-by-admin";
    public const string Reinstated = "subscription.reinstated";
    public const string CanceledByAdmin = "subscription.canceled-by-admin";
    public const string PeriodExtended = "subscription.period-extended";
    public const string PaymentVoided = "payment.voided";
    /// <summary>An operator reviewed a gateway refund or dispute without changing the payment.</summary>
    public const string PaymentIssueReviewed = "payment.issue-reviewed";
    /// <summary>An operator hid or showed the public plans and storage offerings for one account.</summary>
    public const string PublicOfferingVisibilityChanged = "subscription.public-offering-visibility-changed";
    /// <summary>An operator corrected the price an account is charged (its locked price), e.g. after a setup mistake.</summary>
    public const string PriceCorrected = "subscription.price-corrected";
    /// <summary>An operator refunded (part of) an online payment through the gateway. Changes nothing else: voiding is a separate step.</summary>
    public const string PaymentRefundIssued = "payment.refund-issued";

    // Permanent erasure (AccountDeletionRequestService, PlatformAccountDeletionService). Two operators
    // are needed; once approved these vanish with the account, so they only outlive a request that
    // was rejected, canceled or expired.
    public const string DeletionRequested = "account.deletion-requested";
    public const string DeletionCanceled = "account.deletion-canceled";
    public const string DeletionRejected = "account.deletion-rejected";
    public const string DeletionApproved = "account.deletion-approved";
    public const string HardDeleteRequested = "account.hard-delete-requested";
    public const string HardDeleteCompanyDone = "account.hard-delete-company-done";
    public const string HardDeleteFailed = "account.hard-delete-failed";

    // Online checkout (SubscriptionCheckoutService).
    public const string CheckoutStarted = "checkout.started";
    public const string CheckoutPaid = "checkout.paid";
    /// <summary>The gateway reported a payment no checkout here matches -- for an operator to reconcile.</summary>
    public const string CheckoutUnmatched = "checkout.unmatched";
    /// <summary>Paid, but not the amount quoted -- not applied, for an operator to reconcile.</summary>
    public const string CheckoutAmountMismatch = "checkout.amount-mismatch";
    /// <summary>The gateway refunded an online payment. Recorded only: an operator decides what follows.</summary>
    public const string PaymentRefunded = "payment.refunded";
    /// <summary>The payer disputed an online payment. Recorded only: an operator decides what follows.</summary>
    public const string PaymentDisputed = "payment.disputed";
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
        // A row put into PastDue by hand or by an import has no grace end, and would never move
        // on: give it the one the lifecycle would have (period end + grace).
        subscription.GraceEndsAtUtc ??= (subscription.CurrentPeriodEndUtc ?? nowUtc).AddDays(options.GraceDays);
        if (nowUtc < subscription.GraceEndsAtUtc) return null;

        subscription.SubStatus = AccountSubscriptionStatus.Suspended;
        // A lapse, not an operator's suspension: the owner can pay their way out of it.
        subscription.SuspensionReason = null;
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
