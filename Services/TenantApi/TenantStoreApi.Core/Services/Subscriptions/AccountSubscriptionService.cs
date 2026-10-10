using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>What a requested plan change will cost and when it takes effect.</summary>
public sealed record PlanChangeQuote(
    string PlanCode,
    string PlanName,
    BillingCycle BillingCycle,
    PlanChangeTiming Timing,
    DateTime? EffectiveAtUtc,
    decimal AmountDue,
    string Currency,
    bool PricesIncludeVat);

public enum PaymentDueReason
{
    /// <summary>A plan the owner chose, waiting for its first payment.</summary>
    PlanChange,
    /// <summary>The next period of the plan running (or about to run) -- in the renewal window, or overdue.</summary>
    Renewal,
    /// <summary>A lapsed paid subscription (suspended, canceled, expired) coming back.</summary>
    Reactivation,
}

/// <summary>What paying now buys: one period of <see cref="Plan"/> on <see cref="BillingCycle"/>,
/// at this account's price.</summary>
/// <param name="Amount">The whole charge: the plan's price plus <paramref name="AddOnsAmount"/>.</param>
/// <param name="AddOnsAmount">The part of it that renews the company's paid storage blocks.</param>
public sealed record PaymentDue(Plan Plan, BillingCycle BillingCycle, decimal Amount, PaymentDueReason Reason, decimal AddOnsAmount = 0m);

public sealed record RecordPaymentCommand
{
    public decimal Amount { get; init; }
    public PaymentMethod Method { get; init; }
    public string? Reference { get; init; }
    public DateTime? PaidAtUtc { get; init; }
    /// <summary>Unset: the pending change's plan if one awaits payment, otherwise the current plan.</summary>
    public string? PlanCode { get; init; }
    public BillingCycle? BillingCycle { get; init; }
    public string? Notes { get; init; }
}

public sealed record SetPlanCommand
{
    public string PlanCode { get; init; } = string.Empty;
    public BillingCycle? BillingCycle { get; init; }
    /// <summary>Set: the account becomes Active, paid up to this date.</summary>
    public DateTime? PeriodEndUtc { get; init; }
}

public sealed record AddOverrideCommand
{
    public Guid? TenantId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
}

public sealed record AddDiscountCommand
{
    public decimal? Percent { get; init; }
    public decimal? FixedAmount { get; init; }
    public string? Reason { get; init; }
    public DateTime? ValidUntilUtc { get; init; }
}

/// <summary>
/// Commands on an account's subscription -- the owner's (start trial, change plan, cancel, resume)
/// and the platform's (record payment, set plan, extend trial, overrides, discounts). Time-driven
/// transitions belong to <see cref="SubscriptionStateMachine"/>. Doesn't commit: the caller owns
/// the unit of work, so the change, its billing event and its SubscriptionChanged message commit together.
/// </summary>
public class AccountSubscriptionService
{
    private readonly IUnitOfWorkService _uow;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly SubscriptionUsageService _usage;
    private readonly EntitlementResolverService _resolver;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;

    public AccountSubscriptionService(
        IUnitOfWorkService uow,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        SubscriptionUsageService usage,
        EntitlementResolverService resolver,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _events = events;
        _notifier = notifier;
        _usage = usage;
        _resolver = resolver;
        _options = options.Value;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    // ── Queries ──────────────────────────────────────────────────────────────────────────────

    public Task<AccountSubscription?> FindAsync(Guid ownerUserId, CancellationToken token = default) =>
        Context.AccountSubscriptions
            .Include(s => s.Plan).ThenInclude(p => p.Entitlements)
            .Include(s => s.Overrides)
            .Include(s => s.StorageAddOns)
            .FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token);

    private async Task<AccountSubscription> RequireAsync(Guid ownerUserId, CancellationToken token) =>
        await FindAsync(ownerUserId, token)
        ?? throw new KeyNotFoundException("This account has no subscription.");

    /// <summary>The plans one owner may pick: the public catalog, the private plans offered to them
    /// (until the offer expires), and a private plan they are on or moving to -- so it stays on their
    /// screen after the offer lapses. With no owner, only the public catalog.</summary>
    public async Task<List<Plan>> GetVisiblePlansAsync(Guid? ownerUserId, CancellationToken token = default)
    {
        var owner = ownerUserId ?? Guid.Empty;
        var now = Now;

        var offers = await Context.PlanOffers.AsNoTracking()
            .Where(o => o.OwnerUserId == owner)
            .Select(o => new { o.PlanId, Active = o.ExpiresAtUtc == null || o.ExpiresAtUtc > now })
            .ToListAsync(token);
        var held = await Context.AccountSubscriptions.AsNoTracking()
            .Where(s => s.OwnerUserId == owner)
            .Select(s => new { s.PlanId, s.PendingPlanId })
            .FirstOrDefaultAsync(token);

        // A held plan is shown only when it was offered to them: a retired plan nobody offered
        // (e.g. the legacy trial) never becomes a card.
        var visibleIds = offers.Where(o => o.Active).Select(o => o.PlanId).ToHashSet();
        foreach (var heldId in new[] { held?.PlanId, held?.PendingPlanId })
            if (heldId is { } id && offers.Any(o => o.PlanId == id)) visibleIds.Add(id);

        return await Context.Plans.AsNoTracking()
            .Include(p => p.Entitlements)
            .Where(p => p.IsPublic || visibleIds.Contains(p.Id))
            .OrderBy(p => p.SortOrder)
            .ToListAsync(token);
    }

    public Task<Plan?> FindPlanAsync(Guid planId, CancellationToken token = default) =>
        Context.Plans.AsNoTracking().Include(p => p.Entitlements).FirstOrDefaultAsync(p => p.Id == planId, token);

    /// <summary>The plan with this code. <paramref name="forOwner"/> set: it must be one that owner may
    /// pick (see <see cref="GetVisiblePlansAsync"/>), otherwise any plan.</summary>
    private async Task<Plan> RequirePlanAsync(string code, Guid? forOwner, CancellationToken token)
    {
        var plan = await Context.Plans.Include(p => p.Entitlements)
            .FirstOrDefaultAsync(p => p.Code == code, token);
        if (plan == null || (forOwner is { } owner && !await IsVisibleToAsync(plan, owner, token)))
            throw new SubscriptionRuleException($"Unknown plan '{code}'.");
        return plan;
    }

    private async Task<bool> IsVisibleToAsync(Plan plan, Guid ownerUserId, CancellationToken token)
    {
        if (plan.IsPublic) return true;
        var now = Now;
        return await Context.PlanOffers.AnyAsync(o => o.PlanId == plan.Id
            && o.OwnerUserId == ownerUserId
            && (o.ExpiresAtUtc == null || o.ExpiresAtUtc > now), token);
    }

    // ── Private plan offers (platform) ───────────────────────────────────────────────────────

    public Task<List<PlanOffer>> GetOffersAsync(string planCode, CancellationToken token = default) =>
        Context.PlanOffers.AsNoTracking()
            .Where(o => o.Plan.Code == planCode)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(token);

    /// <summary>Offers a plan to one owner, or changes when an existing offer expires. Idempotent.</summary>
    public async Task<PlanOffer> OfferPlanAsync(
        string planCode, Guid ownerUserId, DateTime? expiresAtUtc, string? actor, CancellationToken token = default)
    {
        var plan = await RequirePlanAsync(planCode, forOwner: null, token);
        var subscription = await RequireAsync(ownerUserId, token);

        var offer = await Context.PlanOffers.FirstOrDefaultAsync(o => o.PlanId == plan.Id && o.OwnerUserId == ownerUserId, token);
        if (offer == null)
        {
            offer = new PlanOffer { Id = Guid.CreateVersion7(), PlanId = plan.Id, OwnerUserId = ownerUserId, CreatedBy = actor };
            await Context.PlanOffers.AddAsync(offer, token);
        }
        offer.ExpiresAtUtc = expiresAtUtc;

        await _events.AddAsync(subscription, BillingEventTypes.PlanOffered,
            new { plan = plan.Code, expiresAtUtc }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.PlanOffered, token);
        return offer;
    }

    /// <summary>Withdraws an offer. An owner already on the plan stays on it.</summary>
    public async Task RevokeOfferAsync(string planCode, Guid ownerUserId, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireAsync(ownerUserId, token);
        var offer = await Context.PlanOffers.FirstOrDefaultAsync(
            o => o.Plan.Code == planCode && o.OwnerUserId == ownerUserId, token)
            ?? throw new KeyNotFoundException("This plan isn't offered to that account.");

        Context.PlanOffers.Remove(offer);
        await _events.AddAsync(subscription, BillingEventTypes.PlanOfferRevoked,
            new { plan = planCode }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.PlanOfferRevoked, token);
    }

    /// <summary>The account's payments, newest first. Voided ones are left out unless asked for: to the
    /// owner a voided payment never happened.</summary>
    public Task<List<SubscriptionPayment>> GetPaymentsAsync(Guid subscriptionId, bool includeVoided = false, CancellationToken token = default) =>
        Context.SubscriptionPayments.AsNoTracking()
            .Where(p => p.SubscriptionId == subscriptionId && (includeVoided || p.VoidedAtUtc == null))
            .OrderByDescending(p => p.PaidAtUtc)
            .ToListAsync(token);

    public Task<List<SubscriptionDiscount>> GetActiveDiscountsAsync(Guid subscriptionId, CancellationToken token = default)
    {
        var now = Now;
        return Context.SubscriptionDiscounts.AsNoTracking()
            .Where(d => d.SubscriptionId == subscriptionId && (d.ValidUntilUtc == null || d.ValidUntilUtc > now))
            .ToListAsync(token);
    }

    /// <summary>The price for a plan and cycle: this account's locked price when it's the plan it's
    /// already paying for, otherwise the plan's current price; less any active discounts.</summary>
    public async Task<decimal> PriceForAsync(AccountSubscription subscription, Plan plan, BillingCycle cycle, CancellationToken token = default)
    {
        var listPrice = plan.Id == subscription.PlanId
            ? (cycle == BillingCycle.Annual ? subscription.LockedAnnualPrice : subscription.LockedMonthlyPrice)
            : null;
        var price = listPrice ?? (cycle == BillingCycle.Annual ? plan.AnnualPrice : plan.MonthlyPrice);

        foreach (var discount in await GetActiveDiscountsAsync(subscription.Id, token))
        {
            if (discount.Percent is { } percent) price -= Math.Round(price * percent / 100m, 2);
            if (discount.FixedAmount is { } amount) price -= amount;
        }
        return Math.Max(0, price);
    }

    /// <summary>
    /// What the owner can pay for right now, or null when nothing is due: a chosen plan waiting for
    /// payment; the next period once the renewal window opens (on the plan a scheduled downgrade
    /// will switch to, if there is one), or when it's overdue; a lapsed paid subscription coming
    /// back. A trial -- running or ended -- with no plan chosen has nothing due: choose one first.
    /// </summary>
    public async Task<PaymentDue?> GetPaymentDueAsync(AccountSubscription subscription, CancellationToken token = default)
    {
        var now = Now;
        var pendingCycle = subscription.PendingBillingCycle ?? subscription.BillingCycle;
        Guid planId;
        BillingCycle cycle;
        PaymentDueReason reason;

        if (subscription.PendingChangeTiming == PlanChangeTiming.OnPayment && subscription.PendingPlanId is { } chosen)
        {
            (planId, cycle, reason) = (chosen, pendingCycle, PaymentDueReason.PlanChange);
        }
        else if (subscription.SubStatus == AccountSubscriptionStatus.Active
                 && !subscription.CancelAtPeriodEnd
                 && subscription.CurrentPeriodEndUtc is { } periodEnd
                 && periodEnd - now <= TimeSpan.FromDays(_options.RenewalWindowDays))
        {
            (planId, cycle) = subscription.PendingChangeTiming == PlanChangeTiming.AtPeriodEnd && subscription.PendingPlanId is { } next
                ? (next, pendingCycle)
                : (subscription.PlanId, subscription.BillingCycle);
            reason = PaymentDueReason.Renewal;
        }
        else if (subscription.SubStatus == AccountSubscriptionStatus.PastDue)
        {
            (planId, cycle, reason) = (subscription.PlanId, subscription.BillingCycle, PaymentDueReason.Renewal);
        }
        else if (subscription.SubStatus is AccountSubscriptionStatus.Suspended or AccountSubscriptionStatus.Canceled or AccountSubscriptionStatus.Expired
                 && subscription.CurrentPeriodEndUtc != null)
        {
            (planId, cycle, reason) = (subscription.PlanId, subscription.BillingCycle, PaymentDueReason.Reactivation);
        }
        else
        {
            return null;
        }

        var plan = planId == subscription.PlanId
            ? subscription.Plan
            : await Context.Plans.Include(p => p.Entitlements).FirstOrDefaultAsync(p => p.Id == planId, token);
        if (plan == null) return null;
        // Storage blocks renew with the plan, so paying for a period pays for them too.
        var addOns = StorageAddOnPricing.RenewalAmount(subscription, cycle);
        return new PaymentDue(plan, cycle, await PriceForAsync(subscription, plan, cycle, token) + addOns, reason, addOns);
    }

    // ── Owner commands ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the account's one trial (full Premium) when it creates its first company. An account
    /// that already has a subscription -- trialing, paid or expired -- gets nothing new: a second
    /// company never restarts a trial.
    /// </summary>
    public async Task<AccountSubscription?> StartTrialIfFirstCompanyAsync(
        Guid ownerUserId, string? ownerEmail, CancellationToken token = default)
    {
        if (ownerUserId == Guid.Empty) return null;
        if (await Context.AccountSubscriptions.AnyAsync(s => s.OwnerUserId == ownerUserId, token)) return null;
        if (Context.AccountSubscriptions.Local.Any(s => s.OwnerUserId == ownerUserId)) return null;

        var plan = await RequirePlanAsync(_options.TrialPlanCode, forOwner: null, token);
        var now = Now;
        var subscription = new AccountSubscription
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = ownerUserId,
            PlanId = plan.Id,
            Plan = plan,
            SubStatus = AccountSubscriptionStatus.Trialing,
            BillingCycle = BillingCycle.Monthly,
            TrialEndsAtUtc = now.AddDays(_options.TrialDays),
        };
        await Context.AccountSubscriptions.AddAsync(subscription, token);
        await _events.AddAsync(subscription, BillingEventTypes.TrialStarted,
            new { plan = plan.Code, subscription.TrialEndsAtUtc }, token: token);

        await FlagRepeatTrialAsync(subscription, ownerEmail, token);
        return subscription;
    }

    // One trial per business: a second account from the same company email domain is flagged for
    // the platform to review -- not blocked, since two people at one firm can legitimately sign up.
    private async Task FlagRepeatTrialAsync(AccountSubscription subscription, string? ownerEmail, CancellationToken token)
    {
        var domain = ownerEmail?.Split('@') is [_, var d] && !string.IsNullOrWhiteSpace(d) ? d.Trim().ToLowerInvariant() : null;
        if (domain == null || _options.FreeEmailDomains.Contains(domain, StringComparer.OrdinalIgnoreCase)) return;

        var suffix = "@" + domain;
        var otherOwners = await Context.Memberships.AsNoTracking()
            .Where(m => m.IsHidden
                && m.UserId != subscription.OwnerUserId
                && m.InvitedEmail != null
                && m.InvitedEmail.EndsWith(suffix))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(token);
        if (otherOwners.Count == 0) return;

        var hasTrialed = await Context.AccountSubscriptions.AsNoTracking()
            .AnyAsync(s => otherOwners.Contains(s.OwnerUserId), token);
        if (!hasTrialed) return;

        await _events.AddAsync(subscription, BillingEventTypes.TrialRepeatDomainFlagged,
            new { domain, otherOwners }, token: token);
    }

    /// <summary>
    /// The owner picks a plan. Moving up (or to annual), or choosing any plan while not on a paid
    /// active one, waits for payment; moving down takes effect at the end of the paid period. Either
    /// way the account must already fit the new plan's limits.
    /// </summary>
    public async Task<PlanChangeQuote> RequestPlanChangeAsync(
        Guid ownerUserId, string planCode, BillingCycle cycle, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireAsync(ownerUserId, token);
        var target = await RequirePlanAsync(planCode, forOwner: ownerUserId, token);
        var current = subscription.Plan;

        var isPaidActive = subscription.SubStatus == AccountSubscriptionStatus.Active;
        if (isPaidActive && target.Id == subscription.PlanId && cycle == subscription.BillingCycle)
            throw new SubscriptionRuleException("You're already on this plan.");

        await EnsureFitsAsync(subscription, target, token);

        var isDowngrade = target.SortOrder < current.SortOrder
            || (target.Id == current.Id && cycle == BillingCycle.Monthly && subscription.BillingCycle == BillingCycle.Annual);
        var timing = isPaidActive && isDowngrade ? PlanChangeTiming.AtPeriodEnd : PlanChangeTiming.OnPayment;

        subscription.PendingPlanId = target.Id;
        subscription.PendingBillingCycle = cycle;
        subscription.PendingChangeTiming = timing;
        subscription.PendingChangeEffectiveAtUtc = timing == PlanChangeTiming.AtPeriodEnd ? subscription.CurrentPeriodEndUtc : null;
        Touch(subscription);

        var quote = new PlanChangeQuote(
            target.Code, target.Name, cycle, timing, subscription.PendingChangeEffectiveAtUtc,
            await PriceForAsync(subscription, target, cycle, token), target.Currency, target.PricesIncludeVat);

        await _events.AddAsync(subscription, BillingEventTypes.PlanChangeRequested, quote, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.PlanChangeRequested, token);
        return quote;
    }

    public async Task WithdrawPlanChangeAsync(Guid ownerUserId, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.PendingPlanId == null) throw new SubscriptionRuleException("There's no plan change to withdraw.");

        ActiveState.ClearPendingChange(subscription);
        Touch(subscription);
        await _events.AddAsync(subscription, BillingEventTypes.PlanChangeWithdrawn, actor: actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.PlanChangeWithdrawn, token);
    }

    /// <summary>Cancel: a paid subscription keeps full access to the end of its period; one already
    /// past due ends now.</summary>
    public async Task CancelAsync(Guid ownerUserId, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireAsync(ownerUserId, token);
        var now = Now;
        switch (subscription.SubStatus)
        {
            case AccountSubscriptionStatus.Active when !subscription.CancelAtPeriodEnd:
                subscription.CancelAtPeriodEnd = true;
                await _events.AddAsync(subscription, BillingEventTypes.CancelRequested,
                    new { effectiveAtUtc = subscription.CurrentPeriodEndUtc }, actor, token: token);
                break;
            case AccountSubscriptionStatus.PastDue:
                subscription.SubStatus = AccountSubscriptionStatus.Canceled;
                subscription.CanceledAtUtc = now;
                subscription.PurgeAfterUtc = now.AddDays(_options.PurgeAfterDays);
                await _events.AddAsync(subscription, BillingEventTypes.Canceled, actor: actor, token: token);
                break;
            default:
                throw new SubscriptionRuleException("There's no paid subscription to cancel.");
        }
        Touch(subscription);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.CancelRequested, token);
    }

    public async Task ResumeAsync(Guid ownerUserId, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.SubStatus != AccountSubscriptionStatus.Active || !subscription.CancelAtPeriodEnd)
            throw new SubscriptionRuleException("There's no cancellation to undo.");

        subscription.CancelAtPeriodEnd = false;
        Touch(subscription);
        await _events.AddAsync(subscription, BillingEventTypes.Resumed, actor: actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.Resumed, token);
    }

    // ── Platform commands ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a payment for one billing period. Paying for the plan and cycle already running,
    /// before it ends, renews it: the next period starts when this one ends. So does paying ahead
    /// for a downgrade scheduled for the period end -- the scheduled change still switches the
    /// plan when the current period ends. Anything else -- a trial, an upgrade awaiting payment,
    /// an expired/suspended/canceled account -- activates the paid plan from now. (Proration is
    /// phase 2.)
    /// </summary>
    public async Task<SubscriptionPayment> RecordPaymentAsync(
        Guid ownerUserId, RecordPaymentCommand command, string? actor, CancellationToken token = default)
    {
        if (command.Amount < 0) throw new SubscriptionRuleException("Amount can't be negative.");

        var subscription = await RequireAsync(ownerUserId, token);
        var awaitingPayment = subscription.PendingChangeTiming == PlanChangeTiming.OnPayment;
        var target = command.PlanCode != null
            ? await RequirePlanAsync(command.PlanCode, forOwner: null, token)
            :awaitingPayment && subscription.PendingPlanId is { } pendingId
                ? await Context.Plans.Include(p => p.Entitlements).FirstAsync(p => p.Id == pendingId, token)
                : subscription.Plan;
        var cycle = command.BillingCycle
            ?? (awaitingPayment ? subscription.PendingBillingCycle : null)
            ?? subscription.BillingCycle;

        var now = Now;
        var periodRunning = subscription.SubStatus == AccountSubscriptionStatus.Active
            && subscription.CurrentPeriodEndUtc > now;
        var renewal = periodRunning
            && target.Id == subscription.PlanId
            && cycle == subscription.BillingCycle;
        var prepaysScheduledChange = periodRunning
            && subscription.PendingChangeTiming == PlanChangeTiming.AtPeriodEnd
            && target.Id == subscription.PendingPlanId
            && cycle == (subscription.PendingBillingCycle ?? subscription.BillingCycle);

        var periodStart = renewal || prepaysScheduledChange ? subscription.CurrentPeriodEndUtc!.Value : now;
        var periodEnd = AddCycle(periodStart, cycle);

        if (renewal || prepaysScheduledChange)
        {
            subscription.CurrentPeriodEndUtc = periodEnd;
        }
        else
        {
            if (target.Id != subscription.PlanId || subscription.LockedMonthlyPrice == null)
            {
                subscription.LockedMonthlyPrice = target.MonthlyPrice;
                subscription.LockedAnnualPrice = target.AnnualPrice;
            }
            subscription.PlanId = target.Id;
            subscription.Plan = target;
            subscription.BillingCycle = cycle;
            subscription.SubStatus = AccountSubscriptionStatus.Active;
            subscription.CurrentPeriodStartUtc = periodStart;
            subscription.CurrentPeriodEndUtc = periodEnd;
            subscription.GraceEndsAtUtc = null;
            subscription.CanceledAtUtc = null;
            subscription.PurgeAfterUtc = null;
            // Paying for a plan settles whatever change was pending.
            ActiveState.ClearPendingChange(subscription);
        }
        // A payment is a commitment to continue.
        subscription.CancelAtPeriodEnd = false;
        Touch(subscription);

        var payment = new SubscriptionPayment
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            PlanId = target.Id,
            BillingCycle = cycle,
            Amount = command.Amount,
            Currency = target.Currency,
            Method = command.Method,
            Reference = command.Reference,
            PaidAtUtc = command.PaidAtUtc ?? now,
            PeriodStartUtc = periodStart,
            PeriodEndUtc = periodEnd,
            RecordedBy = actor,
            Notes = command.Notes,
        };
        await Context.SubscriptionPayments.AddAsync(payment, token);
        await _events.AddAsync(subscription, BillingEventTypes.PaymentRecorded,
            new { payment.Id, payment.Amount, plan = target.Code, cycle, periodStart, periodEnd, renewal, prepaysScheduledChange }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.PaymentRecorded, token);
        return payment;
    }

    /// <summary>Puts an account on a plan directly (creating its subscription if it has none).
    /// With <see cref="SetPlanCommand.PeriodEndUtc"/>, it's Active and paid up to then.</summary>
    public async Task<AccountSubscription> SetPlanAsync(
        Guid ownerUserId, SetPlanCommand command, string? actor, CancellationToken token = default)
    {
        var plan = await RequirePlanAsync(command.PlanCode, forOwner: null, token);
        var now = Now;
        var subscription = await FindAsync(ownerUserId, token);
        if (subscription == null)
        {
            subscription = new AccountSubscription
            {
                Id = Guid.CreateVersion7(),
                OwnerUserId = ownerUserId,
                SubStatus = AccountSubscriptionStatus.Expired,
            };
            await Context.AccountSubscriptions.AddAsync(subscription, token);
        }

        subscription.PlanId = plan.Id;
        subscription.Plan = plan;
        subscription.BillingCycle = command.BillingCycle ?? subscription.BillingCycle;
        subscription.LockedMonthlyPrice = plan.MonthlyPrice;
        subscription.LockedAnnualPrice = plan.AnnualPrice;
        ActiveState.ClearPendingChange(subscription);
        if (command.PeriodEndUtc is { } periodEnd)
        {
            if (periodEnd <= now) throw new SubscriptionRuleException("The period must end in the future.");
            subscription.SubStatus = AccountSubscriptionStatus.Active;
            subscription.CurrentPeriodStartUtc = now;
            subscription.CurrentPeriodEndUtc = periodEnd;
            subscription.GraceEndsAtUtc = null;
            subscription.CanceledAtUtc = null;
            subscription.PurgeAfterUtc = null;
            subscription.CancelAtPeriodEnd = false;
        }
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.PlanSetByAdmin,
            new { plan = plan.Code, subscription.BillingCycle, command.PeriodEndUtc }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.PlanSetByAdmin, token);
        return subscription;
    }

    /// <summary>One extension per account, of a trial that's running or has just expired unpaid.</summary>
    public async Task ExtendTrialAsync(Guid ownerUserId, int days, string? actor, CancellationToken token = default)
    {
        if (days < 1 || days > _options.MaxTrialExtensionDays)
            throw new SubscriptionRuleException($"A trial can be extended by 1 to {_options.MaxTrialExtensionDays} days.");

        var subscription = await RequireAsync(ownerUserId, token);
        if (subscription.TrialExtended) throw new SubscriptionRuleException("This trial was already extended once.");
        var wasNeverPaid = subscription.CurrentPeriodEndUtc == null;
        if (!(subscription.SubStatus == AccountSubscriptionStatus.Trialing
              || (subscription.SubStatus == AccountSubscriptionStatus.Expired && wasNeverPaid)))
            throw new SubscriptionRuleException("Only a trial can be extended.");

        var now = Now;
        var from = subscription.TrialEndsAtUtc is { } ends && ends > now ? ends : now;
        subscription.TrialEndsAtUtc = from.AddDays(days);
        subscription.SubStatus = AccountSubscriptionStatus.Trialing;
        subscription.TrialExtended = true;
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.TrialExtended,
            new { days, subscription.TrialEndsAtUtc }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.TrialExtended, token);
    }

    /// <summary>Adds (or replaces) an exception to the plan, account-wide or for one company.</summary>
    public async Task<SubscriptionOverride> AddOverrideAsync(
        Guid ownerUserId, AddOverrideCommand command, string? actor, CancellationToken token = default)
    {
        var key = command.Key.Trim();
        var value = command.Value.Trim();
        if (!EntitlementKeys.IsKnown(key)) throw new SubscriptionRuleException($"Unknown entitlement '{key}'.");
        if (!EntitlementSnapshotBuilder.IsValidValue(key, value))
            throw new SubscriptionRuleException($"'{value}' isn't a valid value for {key}.");
        if (command.TenantId is { } tenantId)
        {
            if (key is EntitlementKeys.CompaniesMax or EntitlementKeys.StorageMaxMb)
                throw new SubscriptionRuleException($"{key} applies to the whole account, not one company.");
            var owned = await Context.Tenants.AnyAsync(t => t.Id == tenantId && t.UserId == ownerUserId, token);
            if (!owned) throw new SubscriptionRuleException("That company isn't owned by this account.");
        }

        var subscription = await RequireAsync(ownerUserId, token);
        foreach (var replaced in subscription.Overrides.Where(o => o.TenantId == command.TenantId && o.Key == key).ToList())
            Context.SubscriptionOverrides.Remove(replaced);

        var entry = new SubscriptionOverride
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            TenantId = command.TenantId,
            Key = key,
            Value = value,
            Reason = command.Reason,
            ExpiresAtUtc = command.ExpiresAtUtc,
            CreatedBy = actor,
        };
        await Context.SubscriptionOverrides.AddAsync(entry, token);
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.OverrideAdded,
            new { entry.Id, entry.TenantId, key, value, command.Reason, command.ExpiresAtUtc }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.OverrideAdded, token);
        return entry;
    }

    public async Task RemoveOverrideAsync(Guid ownerUserId, Guid overrideId, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireAsync(ownerUserId, token);
        var entry = subscription.Overrides.FirstOrDefault(o => o.Id == overrideId)
            ?? throw new KeyNotFoundException("Override not found.");
        Context.SubscriptionOverrides.Remove(entry);
        Touch(subscription);

        await _events.AddAsync(subscription, BillingEventTypes.OverrideRemoved,
            new { entry.Id, entry.TenantId, entry.Key, entry.Value }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.OverrideRemoved, token);
    }

    public async Task<SubscriptionDiscount> AddDiscountAsync(
        Guid ownerUserId, AddDiscountCommand command, string? actor, CancellationToken token = default)
    {
        if ((command.Percent == null) == (command.FixedAmount == null))
            throw new SubscriptionRuleException("Give either a percentage or a fixed amount.");
        if (command.Percent is < 0 or > 100) throw new SubscriptionRuleException("A percentage must be 0 to 100.");
        if (command.FixedAmount is < 0) throw new SubscriptionRuleException("An amount can't be negative.");

        var subscription = await RequireAsync(ownerUserId, token);
        var discount = new SubscriptionDiscount
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            Percent = command.Percent,
            FixedAmount = command.FixedAmount,
            Reason = command.Reason,
            ValidUntilUtc = command.ValidUntilUtc,
            CreatedBy = actor,
        };
        await Context.SubscriptionDiscounts.AddAsync(discount, token);
        await _events.AddAsync(subscription, BillingEventTypes.DiscountAdded,
            new { discount.Id, command.Percent, command.FixedAmount, command.Reason, command.ValidUntilUtc }, actor, token: token);
        return discount;
    }

    public async Task RemoveDiscountAsync(Guid ownerUserId, Guid discountId, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireAsync(ownerUserId, token);
        var discount = await Context.SubscriptionDiscounts
            .FirstOrDefaultAsync(d => d.Id == discountId && d.SubscriptionId == subscription.Id, token)
            ?? throw new KeyNotFoundException("Discount not found.");
        Context.SubscriptionDiscounts.Remove(discount);
        await _events.AddAsync(subscription, BillingEventTypes.DiscountRemoved, new { discount.Id }, actor, token: token);
    }

    // ── Limits ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Every limit the account would be over on <paramref name="target"/> (keeping its
    /// overrides). A count that couldn't be checked is a violation too -- fail closed.</summary>
    public async Task<List<LimitViolation>> FindViolationsAsync(AccountSubscription subscription, Plan target, CancellationToken token = default)
    {
        var violations = new List<LimitViolation>();

        var account = _resolver.ResolveAgainstPlan(subscription, target, Guid.Empty);
        var companies = await _usage.CountOwnedCompaniesAsync(subscription.OwnerUserId, token);
        if (!account.Allows(EntitlementKeys.CompaniesMax, companies, adding: 0))
            violations.Add(new(EntitlementKeys.CompaniesMax, null, null, companies, account.LimitOf(EntitlementKeys.CompaniesMax)));

        var usage = await _usage.GetCompanyUsageAsync(subscription.OwnerUserId, token);

        // Storage is one pool for the whole account: what all its companies store, against the account's
        // limit (plan plus blocks). Any company that couldn't be measured makes the total unknown.
        if (account.LimitOf(EntitlementKeys.StorageMaxMb) is { } storageMax)
        {
            var parts = usage.Select(c => c.Used.GetValueOrDefault(EntitlementKeys.StorageMaxMb)).ToList();
            if (parts.Any(p => p is null)) violations.Add(new(EntitlementKeys.StorageMaxMb, null, null, 0, null));
            else if (parts.Sum(p => p!.Value) is var total && total > storageMax)
                violations.Add(new(EntitlementKeys.StorageMaxMb, null, null, total, storageMax));
        }

        foreach (var company in usage)
        {
            var snapshot = _resolver.ResolveAgainstPlan(subscription, target, company.TenantId);
            foreach (var (key, used) in company.Limits.Where(kv => kv.Key != EntitlementKeys.StorageMaxMb))
            {
                var max = snapshot.LimitOf(key);
                if (max == null) continue;
                if (used is not { } count) violations.Add(new(key, company.TenantId, company.CompanyName, 0, null));
                else if (count > max) violations.Add(new(key, company.TenantId, company.CompanyName, count, max));
            }
        }
        return violations;
    }

    private async Task EnsureFitsAsync(AccountSubscription subscription, Plan target, CancellationToken token)
    {
        var violations = await FindViolationsAsync(subscription, target, token);
        if (violations.Count > 0) throw new PlanLimitExceededException(violations);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    public static DateTime AddCycle(DateTime start, BillingCycle cycle) =>
        cycle == BillingCycle.Annual ? start.AddYears(1) : start.AddMonths(1);

    private static void Touch(AccountSubscription subscription) => subscription.ConcurrencyStamp = Guid.NewGuid();
}
