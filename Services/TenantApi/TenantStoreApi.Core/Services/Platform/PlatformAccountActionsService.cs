using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>
/// What a platform operator can do to an account's subscription beyond what the owner can:
/// suspend, reinstate, cancel at once, grant a complimentary period, and void a payment recorded in
/// error. Every action needs a reason (kept in the billing history with the operator who did it) and
/// publishes <c>SubscriptionChanged</c> so every service re-reads the account's access. Like
/// <see cref="AccountSubscriptionService"/>, it doesn't commit: the caller owns the unit of work.
/// </summary>
public class PlatformAccountActionsService
{
    public const int MaxExtensionDays = 365;
    private const int MaxReasonLength = 500;

    private readonly IUnitOfWorkService _uow;
    private readonly AccountSubscriptionService _subscriptions;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly SubscriptionStateMachine _states;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;

    public PlatformAccountActionsService(
        IUnitOfWorkService uow,
        AccountSubscriptionService subscriptions,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        SubscriptionStateMachine states,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _subscriptions = subscriptions;
        _events = events;
        _notifier = notifier;
        _states = states;
        _options = options.Value;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    /// <summary>Read-only from now on (the same access a lapsed account has) until reinstated or paid.</summary>
    public async Task SuspendAsync(Guid ownerUserId, string? reason, string? actor, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.SubStatus is not (AccountSubscriptionStatus.Trialing or AccountSubscriptionStatus.Active or AccountSubscriptionStatus.PastDue))
            throw new SubscriptionRuleException($"A {subscription.SubStatus} account can't be suspended.");

        var from = subscription.SubStatus;
        subscription.SubStatus = AccountSubscriptionStatus.Suspended;
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.SuspendedByAdmin, new { from, reason }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.SuspendedByAdmin, token);
    }

    /// <summary>
    /// Undoes a suspension, or revives an expired/canceled account whose paid period (or trial) is
    /// still running. When there's nothing left to give back, the operator must record a payment or
    /// set a plan instead -- reinstating never invents access.
    /// </summary>
    public async Task ReinstateAsync(Guid ownerUserId, string? reason, string? actor, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.SubStatus is not (AccountSubscriptionStatus.Suspended or AccountSubscriptionStatus.Canceled or AccountSubscriptionStatus.Expired))
            throw new SubscriptionRuleException($"A {subscription.SubStatus} account has nothing to reinstate.");

        var now = Now;
        var from = subscription.SubStatus;
        if (subscription.CurrentPeriodEndUtc is { } periodEnd && periodEnd > now)
        {
            subscription.SubStatus = AccountSubscriptionStatus.Active;
            subscription.GraceEndsAtUtc = null;
            subscription.CanceledAtUtc = null;
            subscription.PurgeAfterUtc = null;
            subscription.CancelAtPeriodEnd = false;
        }
        else if (subscription.CurrentPeriodEndUtc == null && subscription.TrialEndsAtUtc is { } trialEnd && trialEnd > now)
        {
            subscription.SubStatus = AccountSubscriptionStatus.Trialing;
            subscription.CanceledAtUtc = null;
            subscription.PurgeAfterUtc = null;
        }
        else
        {
            throw new SubscriptionRuleException(
                "The paid period has ended. Record a payment or set a plan to reactivate this account.");
        }
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.Reinstated, new { from, to = subscription.SubStatus, reason }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.Reinstated, token);
    }

    /// <summary>Ends the subscription now (the owner's own cancel keeps access to the end of the paid period).</summary>
    public async Task CancelNowAsync(Guid ownerUserId, string? reason, string? actor, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.SubStatus == AccountSubscriptionStatus.Canceled)
            throw new SubscriptionRuleException("This account is already canceled.");

        var now = Now;
        var from = subscription.SubStatus;
        subscription.SubStatus = AccountSubscriptionStatus.Canceled;
        subscription.CanceledAtUtc = now;
        subscription.PurgeAfterUtc = now.AddDays(_options.PurgeAfterDays);
        subscription.CancelAtPeriodEnd = false;
        subscription.GraceEndsAtUtc = null;
        ActiveState.ClearPendingChange(subscription);
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.CanceledByAdmin, new { from, reason, subscription.PurgeAfterUtc }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.CanceledByAdmin, token);
    }

    /// <summary>A complimentary extension of the paid period (no payment recorded). Active and past-due accounts only.</summary>
    public async Task ExtendPeriodAsync(Guid ownerUserId, int days, string? reason, string? actor, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        if (days < 1 || days > MaxExtensionDays)
            throw new SubscriptionRuleException($"A period can be extended by 1 to {MaxExtensionDays} days.");

        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.SubStatus is not (AccountSubscriptionStatus.Active or AccountSubscriptionStatus.PastDue)
            || subscription.CurrentPeriodEndUtc is not { } oldEnd)
            throw new SubscriptionRuleException("Only an active or past-due account with a paid period can be extended. For a trial use a trial extension.");

        var now = Now;
        var newEnd = oldEnd.AddDays(days);
        subscription.CurrentPeriodEndUtc = newEnd;
        // A downgrade scheduled for the old period end moves with it.
        if (subscription.PendingChangeTiming == PlanChangeTiming.AtPeriodEnd && subscription.PendingChangeEffectiveAtUtc is { } effective)
            subscription.PendingChangeEffectiveAtUtc = effective.AddDays(days);

        if (subscription.SubStatus == AccountSubscriptionStatus.PastDue)
        {
            if (newEnd > now)
            {
                subscription.SubStatus = AccountSubscriptionStatus.Active;
                subscription.GraceEndsAtUtc = null;
            }
            else
            {
                subscription.GraceEndsAtUtc = newEnd.AddDays(_options.GraceDays);
            }
        }
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.PeriodExtended, new { days, from = oldEnd, to = newEnd, reason }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.PeriodExtended, token);
    }

    /// <summary>
    /// Marks a payment recorded in error as void -- never deleted, the money trail stays. If it is the
    /// payment that set the current paid period, that period falls back to the previous live payment's
    /// (or to nothing: the trial if it's still running, otherwise Expired), and whatever time-driven
    /// transition is then due is applied. The plan is not rolled back: use Set plan if it also changed.
    /// </summary>
    public async Task VoidPaymentAsync(Guid paymentId, string? reason, string? actor, VoidKind kind = VoidKind.EntryError, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var payment = await Context.SubscriptionPayments.FirstOrDefaultAsync(p => p.Id == paymentId, token)
            ?? throw new KeyNotFoundException("Payment not found.");
        if (payment.VoidedAtUtc != null) throw new SubscriptionRuleException("This payment is already void.");

        var subscription = await Context.AccountSubscriptions
            .Include(s => s.Plan).ThenInclude(p => p.Entitlements)
            .Include(s => s.Overrides)
            .FirstAsync(s => s.Id == payment.SubscriptionId, token);

        var now = Now;
        payment.VoidedAtUtc = now;
        payment.VoidedBy = actor;
        payment.VoidReason = reason;
        payment.VoidKind = kind;

        var transitions = new List<SubscriptionTransition>();
        var definedCurrentPeriod = subscription.CurrentPeriodEndUtc == payment.PeriodEndUtc;
        if (definedCurrentPeriod)
        {
            var previous = await Context.SubscriptionPayments.AsNoTracking()
                .Where(p => p.SubscriptionId == subscription.Id && p.Id != payment.Id && p.VoidedAtUtc == null
                    && p.PeriodEndUtc < payment.PeriodEndUtc)
                .OrderByDescending(p => p.PeriodEndUtc)
                .FirstOrDefaultAsync(token);

            subscription.CancelAtPeriodEnd = false;
            if (previous != null)
            {
                subscription.CurrentPeriodStartUtc = previous.PeriodStartUtc;
                subscription.CurrentPeriodEndUtc = previous.PeriodEndUtc;
                transitions = _states.Advance(subscription, now, _options);
            }
            else
            {
                subscription.CurrentPeriodStartUtc = null;
                subscription.CurrentPeriodEndUtc = null;
                subscription.GraceEndsAtUtc = null;
                subscription.SubStatus = subscription.TrialEndsAtUtc is { } trialEnd && trialEnd > now
                    ? AccountSubscriptionStatus.Trialing
                    : AccountSubscriptionStatus.Expired;
            }
        }
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.PaymentVoided,
            new { payment.Id, payment.Amount, reason, kind = kind.ToString(), periodWasReverted = definedCurrentPeriod, status = subscription.SubStatus },
            actor, token: token);
        foreach (var transition in transitions)
            await _events.AddAsync(subscription, transition.EventType, new { transition.From, transition.To, cause = "payment voided" }, actor, token: token);
        await _notifier.PublishAsync(subscription.OwnerUserId, BillingEventTypes.PaymentVoided, token);
    }

    /// <summary>
    /// Stops the account's data being purged, and its archive destroyed, until released -- for a
    /// pending tax audit, protest, refund claim or labor case, where the records must be kept past
    /// the usual period.
    /// </summary>
    public async Task SetLegalHoldAsync(Guid ownerUserId, string? reason, string? actor, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.PurgeState == DataPurgeState.ArchiveDestroyed)
            throw new SubscriptionRuleException("This account's data has already been destroyed; there is nothing to hold.");
        if (subscription.LegalHold)
            throw new SubscriptionRuleException("This account is already on legal hold.");

        subscription.LegalHold = true;
        subscription.LegalHoldReason = reason;
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.LegalHoldSet, new { reason }, actor, token: token);
    }

    /// <summary>
    /// Lifts a legal hold. If the purge date passed during the hold, it moves to the minimum notice
    /// from now, so the owner is warned (and can pay or export) before anything is deleted.
    /// </summary>
    public async Task ReleaseLegalHoldAsync(Guid ownerUserId, string? reason, string? actor, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var subscription = await RequireAsync(ownerUserId, token);
        if (!subscription.LegalHold)
            throw new SubscriptionRuleException("This account isn't on legal hold.");

        var earliest = Now.AddDays(_options.MinPurgeNoticeDays);
        subscription.LegalHold = false;
        subscription.LegalHoldReason = null;
        if (subscription.PurgeState is DataPurgeState.Scheduled or DataPurgeState.InProgress
            && subscription.PurgeAfterUtc is { } purgeAfter && purgeAfter < earliest)
        {
            subscription.PurgeAfterUtc = earliest;
            subscription.PurgeState = DataPurgeState.Scheduled;
        }
        if (subscription.PurgeState == DataPurgeState.Purged
            && subscription.ArchiveRetainUntilUtc is { } retainUntil && retainUntil < earliest)
            subscription.ArchiveRetainUntilUtc = earliest;
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.LegalHoldReleased, new { reason }, actor, token: token);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<AccountSubscription> RequireAsync(Guid ownerUserId, CancellationToken token) =>
        await _subscriptions.FindAsync(ownerUserId, token)
        ?? throw new KeyNotFoundException("This account has no subscription.");

    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 3)
            throw new SubscriptionRuleException("Give a reason (at least 3 characters) — it's kept in the billing history.");
        if (trimmed.Length > MaxReasonLength)
            throw new SubscriptionRuleException($"The reason is too long (max {MaxReasonLength} characters).");
        return trimmed;
    }

    private static void Touch(AccountSubscription subscription) => subscription.ConcurrencyStamp = Guid.NewGuid();
}
