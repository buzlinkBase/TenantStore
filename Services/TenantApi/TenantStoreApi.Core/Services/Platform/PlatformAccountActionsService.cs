using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantStoreApi.Core.Services.Payments;
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
    private readonly IPaymentGateway _gateway;
    private readonly TimeProvider _clock;

    public PlatformAccountActionsService(
        IUnitOfWorkService uow,
        AccountSubscriptionService subscriptions,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        SubscriptionStateMachine states,
        IOptions<SubscriptionOptions> options,
        IPaymentGateway gateway,
        TimeProvider clock)
    {
        _uow = uow;
        _subscriptions = subscriptions;
        _events = events;
        _notifier = notifier;
        _states = states;
        _options = options.Value;
        _gateway = gateway;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    /// <summary>Acknowledges the gateway issue currently awaiting review without changing payment state.</summary>
    public async Task MarkPaymentIssueReviewedAsync(Guid paymentId, string? actor, CancellationToken token = default)
    {
        var checkout = await Context.PaymentCheckouts.AsNoTracking()
            .Where(c => c.SubscriptionPaymentId == paymentId || c.StorageAddOnPaymentId == paymentId)
            .Select(c => new { c.SubscriptionId, c.ProviderPaymentId })
            .FirstOrDefaultAsync(token)
            ?? throw new SubscriptionRuleException("This payment has no associated gateway checkout.");
        if (string.IsNullOrWhiteSpace(checkout.ProviderPaymentId))
            throw new SubscriptionRuleException("This payment has no gateway payment to review.");

        var subscription = await Context.AccountSubscriptions.FirstOrDefaultAsync(s => s.Id == checkout.SubscriptionId, token)
            ?? throw new SubscriptionRuleException("This payment's account subscription could not be found.");
        var ledger = await GatewayIssueLedger.LoadAsync(Context, [subscription.OwnerUserId], token);
        var issueKeys = ledger.UnreviewedIssueKeys(checkout.ProviderPaymentId);
        if (issueKeys.Count == 0)
            throw new SubscriptionRuleException("This payment no longer has an issue awaiting review.");

        foreach (var issueKey in issueKeys)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issueKey)));
            var dedupeKey = $"platform:payment-issue-reviewed:{hash}";
            if (await _events.ExistsAsync(dedupeKey, token)) continue;

            await _events.AddAsync(subscription, BillingEventTypes.PaymentIssueReviewed,
                new { gatewayPaymentId = checkout.ProviderPaymentId, issueKey },
                actor, dedupeKey, token: token);
        }
    }

    /// <summary>Read-only from now on (the same access a lapsed account has) until reinstated or paid.</summary>
    public async Task SuspendAsync(Guid ownerUserId, string? reason, string? actor, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.SubStatus is not (AccountSubscriptionStatus.Trialing or AccountSubscriptionStatus.Active or AccountSubscriptionStatus.PastDue))
            throw new SubscriptionRuleException($"A {subscription.SubStatus} account can't be suspended.");

        var from = subscription.SubStatus;
        subscription.SubStatus = AccountSubscriptionStatus.Suspended;
        subscription.SuspensionReason = reason;
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.SuspendedByAdmin, new { from = from.ToString(), reason }, actor, token: token);
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
        await RequireNotErasingAsync(subscription, "reinstated", alsoCompanies: false, token);
        if (subscription.SubStatus is not (AccountSubscriptionStatus.Suspended or AccountSubscriptionStatus.Canceled or AccountSubscriptionStatus.Expired))
            throw new SubscriptionRuleException($"A {subscription.SubStatus} account has nothing to reinstate.");

        var now = Now;
        var from = subscription.SubStatus;
        subscription.SuspensionReason = null;
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

        await _events.AddAsync(subscription, BillingEventTypes.Reinstated, new { from = from.ToString(), to = subscription.SubStatus.ToString(), reason }, actor, token: token);
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

        await _events.AddAsync(subscription, BillingEventTypes.CanceledByAdmin, new { from = from.ToString(), reason, subscription.PurgeAfterUtc }, actor, token: token);
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
    /// transition is then due is applied. The plan is left alone unless <paramref name="revertPlan"/>: then
    /// the plan and billing cycle go back to the previous live payment's, when they differ.
    /// </summary>
    public async Task VoidPaymentAsync(
        Guid paymentId, string? reason, string? actor, VoidKind kind = VoidKind.EntryError, bool revertPlan = false, CancellationToken token = default)
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
        object? planReverted = null;
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
                if (revertPlan) planReverted = await RevertPlanAsync(subscription, previous, token);
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
            new { payment.Id, payment.Amount, reason, kind = kind.ToString(), periodWasReverted = definedCurrentPeriod, planReverted, status = subscription.SubStatus },
            actor, token: token);
        foreach (var transition in transitions)
            await _events.AddAsync(subscription, transition.EventType, new { transition.From, transition.To, cause = "payment voided" }, actor, token: token);
        await _notifier.PublishAsync(subscription.OwnerUserId, BillingEventTypes.PaymentVoided, token);
    }

    /// <summary>Puts the account back on the plan and cycle <paramref name="previous"/> was paid for. Returns
    /// the plan codes it went from and to, or null when they already match (nothing to revert).</summary>
    private async Task<object?> RevertPlanAsync(AccountSubscription subscription, SubscriptionPayment previous, CancellationToken token)
    {
        if (previous.PlanId == subscription.PlanId && previous.BillingCycle == subscription.BillingCycle) return null;

        var from = subscription.Plan.Code;
        var plan = await Context.Plans.Include(p => p.Entitlements).FirstAsync(p => p.Id == previous.PlanId, token);
        subscription.PlanId = plan.Id;
        subscription.Plan = plan;
        subscription.BillingCycle = previous.BillingCycle;
        subscription.LockedMonthlyPrice = plan.MonthlyPrice;
        subscription.LockedAnnualPrice = plan.AnnualPrice;
        ActiveState.ClearPendingChange(subscription);
        return new { from, to = plan.Code };
    }

    /// <summary>
    /// Gives <paramref name="amount"/> of an online payment back to the payer through the gateway, and
    /// records it in the billing history. That is all it does: the payment stays live and the plan and
    /// period are untouched -- voiding is a separate decision. Only a payment taken through the gateway
    /// can be refunded (a hand-recorded one has no gateway payment), and never more than is left of it;
    /// the gateway is asked for that balance just before, so a double click can't refund twice.
    /// </summary>
    public async Task<GatewayRefund> RefundPaymentAsync(
        Guid paymentId, decimal amount, GatewayRefundReason reason, string? notes, string? actor, CancellationToken token = default)
    {
        if (!_gateway.IsEnabled) throw new SubscriptionRuleException("Online payment isn't configured, so nothing can be refunded.");
        amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m) throw new SubscriptionRuleException("The amount to refund must be more than zero.");
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (notes is { Length: > MaxReasonLength }) throw new SubscriptionRuleException($"Notes can be at most {MaxReasonLength} characters.");

        var checkout = await Context.PaymentCheckouts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == _gateway.Provider && c.ProviderPaymentId != null
                && (c.SubscriptionPaymentId == paymentId || c.StorageAddOnPaymentId == paymentId), token)
            ?? throw new SubscriptionRuleException("This payment wasn't taken through the payment gateway, so it can't be refunded here.");
        var gatewayPaymentId = checkout.ProviderPaymentId!;

        var state = await _gateway.GetPaymentAsync(gatewayPaymentId, token);
        var refundable = state.Amount - state.RefundedAmount;
        if (amount > refundable)
        {
            throw new SubscriptionRuleException(refundable <= 0m
                ? "This payment has already been fully refunded."
                : $"Only {refundable:0.00} of this payment is left to refund.");
        }

        var refund = await _gateway.RefundPaymentAsync(gatewayPaymentId, amount, reason, notes, token);

        var subscription = await Context.AccountSubscriptions.FirstAsync(s => s.Id == checkout.SubscriptionId, token);
        await _events.AddAsync(subscription, BillingEventTypes.PaymentRefundIssued,
            new
            {
                paymentId, gatewayPaymentId, refundId = refund.RefundId, amount = refund.Amount, status = refund.Status,
                reason = reason.ToString(), notes, kind = checkout.Kind.ToString(),
            },
            actor, $"{_gateway.Provider}:refund:{refund.RefundId}", token: token);
        return refund;
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
        await RequireNotErasingAsync(subscription, "put on legal hold", alsoCompanies: true, token);

        subscription.LegalHold = true;
        subscription.LegalHoldReason = reason;
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.LegalHoldSet, new { reason }, actor, token: token);
        // A hold stops the account's use, so every service must re-read its access.
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.LegalHoldSet, token);
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
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.LegalHoldReleased, token);
    }

    /// <summary>
    /// Shows or hides the public plans and storage offerings for one account -- for a client on a custom
    /// plan who should see only what was offered to them. Hiding is refused unless the account has a
    /// private plan to renew with (an unexpired offer, or the plan it holds / is moving to that was
    /// offered to it), otherwise it would be left with nothing to pick.
    /// </summary>
    public async Task SetPublicOfferingVisibilityAsync(Guid ownerUserId, bool show, string? reason, string? actor, CancellationToken token = default)
    {
        reason = RequireReason(reason);
        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.ShowPublicOffering == show)
            throw new SubscriptionRuleException(show ? "This account already sees the public plans." : "The public plans are already hidden for this account.");

        if (!show)
        {
            var now = Now;
            var heldIds = new[] { subscription.PlanId, subscription.PendingPlanId }.OfType<Guid>().ToList();
            var hasPrivatePlan = await Context.PlanOffers.AsNoTracking().AnyAsync(o => o.OwnerUserId == ownerUserId
                && ((o.ExpiresAtUtc == null || o.ExpiresAtUtc > now) || heldIds.Contains(o.PlanId)), token);
            if (!hasPrivatePlan)
                throw new SubscriptionRuleException("Offer this account a plan first, otherwise it would have nothing to renew with.");
        }

        subscription.ShowPublicOffering = show;
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.PublicOfferingVisibilityChanged, new { show, reason }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.PublicOfferingVisibilityChanged, token);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<AccountSubscription> RequireAsync(Guid ownerUserId, CancellationToken token) =>
        await _subscriptions.FindAsync(ownerUserId, token)
        ?? throw new KeyNotFoundException("This account has no subscription.");

    /// <summary>
    /// Nothing may undo or second-guess an approved erasure halfway: data is already gone from some
    /// services, so making the account writable again (or holding records that are partly deleted)
    /// would leave it half there. Retry the erasure instead. <paramref name="alsoCompanies"/>: one
    /// company's erasure counts too (a hold can't keep records that are half deleted).
    /// </summary>
    private async Task RequireNotErasingAsync(AccountSubscription subscription, string action, bool alsoCompanies, CancellationToken token)
    {
        if (subscription.PurgeState is DataPurgeState.HardDeleting or DataPurgeState.HardDeleteFailed)
            throw new SubscriptionRuleException($"This account is being deleted, so it can't be {action}. Retry the deletion instead.");
        if (alsoCompanies && await Context.AccountDeletionRequests.AsNoTracking()
                .AnyAsync(r => r.OwnerUserId == subscription.OwnerUserId && r.TenantId != null && r.Erasure != null, token))
            throw new SubscriptionRuleException($"One of this account's companies is being deleted, so the account can't be {action} until that finishes.");
    }

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
