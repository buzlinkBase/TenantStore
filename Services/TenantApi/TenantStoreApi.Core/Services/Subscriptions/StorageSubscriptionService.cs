using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

public enum StorageDueReason
{
    /// <summary>The next storage period: inside the renewal window, or past due.</summary>
    Renewal,
    /// <summary>Lapsed storage coming back: a new period from the day it's paid.</summary>
    Reactivation,
}

/// <summary>What paying storage now buys: one more period of every paid block, on <see cref="BillingCycle"/>.</summary>
public sealed record StorageRenewalDue(
    BillingCycle BillingCycle,
    decimal Amount,
    string Currency,
    StorageDueReason Reason,
    int Blocks,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    IReadOnlyList<PaymentLineItemSnapshot> LineItems);

/// <summary>Where a new purchase's money goes: the rest of the running storage period, or a new one.</summary>
/// <param name="StartsNewPeriod">No storage period is running: the purchase starts one, at full price.</param>
public sealed record StoragePurchaseSpan(
    BillingCycle BillingCycle,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    bool StartsNewPeriod);

/// <summary>
/// The storage blocks' own billing period, independent of the plan's: its cycle, due date, renewal and
/// lapse. A storage period can be monthly on an annual plan, or the reverse, and runs to its own end
/// whatever the plan does. Blocks themselves (buying, grants, reductions) are <see cref="StorageAddOnService"/>'s.
/// Doesn't commit.
/// </summary>
public class StorageSubscriptionService
{
    private readonly IUnitOfWorkService _uow;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly StorageSubscriptionStateMachine _states;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;

    public StorageSubscriptionService(
        IUnitOfWorkService uow,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        StorageSubscriptionStateMachine states,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _events = events;
        _notifier = notifier;
        _states = states;
        _options = options.Value;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private static IEnumerable<StorageAddOn> PaidBlocks(AccountSubscription subscription) =>
        subscription.StorageAddOns.Where(a => a.DeletedAt == null && !a.IsComplimentary && a.Blocks > 0);

    /// <summary>The paid blocks a renewal would carry into the next storage period (reductions due by then already taken off).</summary>
    public static int BlocksToRenew(AccountSubscription subscription) =>
        PaidBlocks(subscription).Sum(a => StorageAddOnPricing.BlocksAtRenewal(a, subscription.StorageSubscription?.CurrentPeriodEndUtc));

    // ── Purchases ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The span a purchase pays for. A running storage period (active, not yet ended): the rest of it,
    /// on its cycle -- <paramref name="requestedCycle"/> is ignored, so every block shares one due date.
    /// No period yet, or a lapsed one with nothing left owing: a new period from now on
    /// <paramref name="requestedCycle"/> (default: the plan's). Unpaid storage must be renewed first,
    /// otherwise a purchase would bring the unpaid blocks back for free.
    /// </summary>
    public StoragePurchaseSpan SpanForPurchase(AccountSubscription subscription, BillingCycle? requestedCycle)
    {
        var now = Now;
        var storage = subscription.StorageSubscription;
        var status = storage == null ? StorageSubscriptionStatus.Lapsed : StorageSubscriptionStateMachine.StatusAt(storage, now);
        if (status == StorageSubscriptionStatus.Active && storage!.CurrentPeriodEndUtc is { } end)
        {
            var start = storage.CurrentPeriodStartUtc ?? AccountSubscriptionService.AddCycle(end, storage.BillingCycle, -1);
            return new StoragePurchaseSpan(storage.BillingCycle, start, end, StartsNewPeriod: false);
        }
        if (status == StorageSubscriptionStatus.PastDue || (storage != null && PaidBlocks(subscription).Any()))
            throw new SubscriptionRuleException("Your storage renewal is unpaid. Pay it first, then add more storage.");

        var cycle = requestedCycle ?? storage?.BillingCycle ?? subscription.BillingCycle;
        return new StoragePurchaseSpan(cycle, now, AccountSubscriptionService.AddCycle(now, cycle), StartsNewPeriod: true);
    }

    /// <summary>Called when bought blocks are paid for: starts the storage period when the purchase began
    /// one, and keeps a running one going (buying cancels a scheduled end).</summary>
    public async Task<StorageSubscription> ApplyPurchaseAsync(
        AccountSubscription subscription, StoragePurchaseSpan span, CancellationToken token = default)
    {
        var storage = subscription.StorageSubscription;
        if (storage == null)
        {
            storage = new StorageSubscription { Id = Guid.CreateVersion7(), SubscriptionId = subscription.Id };
            await Context.StorageSubscriptions.AddAsync(storage, token);
            subscription.StorageSubscription = storage;
        }
        if (span.StartsNewPeriod)
        {
            storage.BillingCycle = span.BillingCycle;
            storage.PendingBillingCycle = null;
            storage.CurrentPeriodStartUtc = span.PeriodStartUtc;
            storage.CurrentPeriodEndUtc = span.PeriodEndUtc;
        }
        storage.Status = StorageSubscriptionStatus.Active;
        storage.GraceEndsAtUtc = null;
        storage.CancelAtPeriodEnd = false;
        Touch(storage);
        return storage;
    }

    // ── Renewal ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What the owner can pay for storage right now, or null when nothing is due: the next period once
    /// the renewal window opens (unless every paid block is being given up), or one that's past due or
    /// lapsed with paid blocks left. Independent of the plan -- only an operator's suspension stops it.
    /// <paramref name="anyTime"/>: the next period whatever the date (what an auto renew charges, which the
    /// gateway may bill a few days before the window opens).
    /// </summary>
    public async Task<StorageRenewalDue?> GetRenewalDueAsync(
        AccountSubscription subscription, CancellationToken token = default, bool anyTime = false)
    {
        if (AccountSubscriptionService.IsSuspendedByOperator(subscription)) return null;
        var storage = subscription.StorageSubscription;
        if (storage?.CurrentPeriodEndUtc is not { } end) return null;

        var now = Now;
        var status = StorageSubscriptionStateMachine.StatusAt(storage, now);
        StorageDueReason reason;
        DateTime start;
        if (status == StorageSubscriptionStatus.Active)
        {
            if (storage.CancelAtPeriodEnd || (!anyTime && end - now > TimeSpan.FromDays(_options.RenewalWindowDays))) return null;
            (reason, start) = (StorageDueReason.Renewal, end);
        }
        else
        {
            (reason, start) = (status == StorageSubscriptionStatus.PastDue ? StorageDueReason.Renewal : StorageDueReason.Reactivation, now);
        }

        var cycle = storage.PendingBillingCycle ?? storage.BillingCycle;
        var lines = new List<PaymentLineItemSnapshot>();
        var blocks = 0;
        var offerings = await Context.StorageAddOnOfferings.AsNoTracking()
            .Select(o => new { o.Id, o.Name, o.Currency })
            .ToDictionaryAsync(o => o.Id, token);
        foreach (var addOn in PaidBlocks(subscription))
        {
            var held = status == StorageSubscriptionStatus.Active
                ? StorageAddOnPricing.BlocksAtRenewal(addOn, end)
                : StorageAddOnPricing.BlocksAt(addOn, now);
            if (held <= 0) continue;
            blocks += held;
            var name = offerings.GetValueOrDefault(addOn.OfferingId)?.Name;
            lines.Add(new(StorageAddOnService.DescribePayment(held, name), held * StorageAddOnPricing.UnitPrice(addOn, cycle),
                $"{CycleLabel(cycle)} storage"));
        }
        if (blocks == 0) return null;

        var currency = PaidBlocks(subscription).Select(a => offerings.GetValueOrDefault(a.OfferingId)?.Currency).FirstOrDefault(c => c != null) ?? "PHP";
        return new StorageRenewalDue(cycle, lines.Sum(l => l.Amount), currency, reason, blocks,
            start, AccountSubscriptionService.AddCycle(start, cycle), lines);
    }

    /// <summary>
    /// Records one more storage period for every paid block: from the current end when paid before it,
    /// from now when it was past due or lapsed. A cycle switch the owner asked for takes effect here.
    /// Throws when nothing is due -- an early click outside the renewal window renews nothing.
    /// </summary>
    public async Task<StorageAddOnPayment> RecordRenewalAsync(
        Guid ownerUserId, decimal amount, PaymentMethod method, string? reference, string? actor,
        DateTime? paidAtUtc = null, CancellationToken token = default, bool anyTime = false)
    {
        if (amount < 0) throw new SubscriptionRuleException("Amount can't be negative.");
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var due = await GetRenewalDueAsync(subscription, token, anyTime)
            ?? throw new SubscriptionRuleException("There's no storage renewal due.");
        var storage = subscription.StorageSubscription!;
        var now = Now;

        // Reductions due by the time the new period starts become permanent now, so the stored blocks
        // match what was paid for.
        foreach (var (addOn, _, to) in StorageAddOnPricing.ApplyDueReductions(subscription, due.PeriodStartUtc))
            if (to == 0) Context.StorageAddOns.Remove(addOn);

        var from = storage.Status;
        storage.BillingCycle = due.BillingCycle;
        storage.PendingBillingCycle = null;
        storage.CurrentPeriodStartUtc = due.PeriodStartUtc;
        storage.CurrentPeriodEndUtc = due.PeriodEndUtc;
        storage.Status = StorageSubscriptionStatus.Active;
        storage.GraceEndsAtUtc = null;
        storage.CancelAtPeriodEnd = false;
        Touch(storage);

        var payment = new StorageAddOnPayment
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            Kind = StorageAddOnPaymentKind.Renewal,
            BillingCycle = due.BillingCycle,
            AddOnId = Guid.Empty,
            Blocks = due.Blocks,
            Amount = amount,
            Currency = due.Currency,
            Method = method,
            Reference = reference,
            PaidAtUtc = paidAtUtc ?? now,
            PeriodStartUtc = due.PeriodStartUtc,
            PeriodEndUtc = due.PeriodEndUtc,
            RecordedBy = actor,
            LineItemsJson = JsonSerializer.Serialize(due.LineItems),
        };
        await Context.StorageAddOnPayments.AddAsync(payment, token);
        await _events.AddAsync(subscription, BillingEventTypes.StorageRenewed,
            new { payment.Id, amount, cycle = due.BillingCycle, due.Blocks, from = from.ToString(), due.PeriodStartUtc, due.PeriodEndUtc },
            actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageRenewed, token);
        return payment;
    }

    // ── Cycle ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Switches the storage cycle from the next renewal on. Asking for the cycle already running withdraws a pending switch.</summary>
    public async Task<StorageSubscription> RequestCycleChangeAsync(
        Guid ownerUserId, BillingCycle cycle, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var storage = subscription.StorageSubscription
            ?? throw new SubscriptionRuleException("There's no storage billing to change yet -- buy storage first.");
        if (cycle == storage.BillingCycle && storage.PendingBillingCycle == null)
            throw new SubscriptionRuleException($"Storage is already billed {CycleLabel(cycle)}.");

        storage.PendingBillingCycle = cycle == storage.BillingCycle ? null : cycle;
        Touch(storage);
        await _events.AddAsync(subscription,
            storage.PendingBillingCycle == null ? BillingEventTypes.StorageCycleChangeWithdrawn : BillingEventTypes.StorageCycleChangeRequested,
            new { cycle, effectiveAtUtc = storage.CurrentPeriodEndUtc }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageCycleChangeRequested, token);
        return storage;
    }

    public async Task WithdrawCycleChangeAsync(Guid ownerUserId, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var storage = subscription.StorageSubscription;
        if (storage?.PendingBillingCycle == null) throw new SubscriptionRuleException("There's no storage cycle change to withdraw.");

        storage.PendingBillingCycle = null;
        Touch(storage);
        await _events.AddAsync(subscription, BillingEventTypes.StorageCycleChangeWithdrawn, actor: actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageCycleChangeWithdrawn, token);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The hourly job's storage pass: makes due reductions permanent, then moves the storage period on by
    /// date (Active → PastDue → Lapsed). Records the events and returns whether anything changed, so the
    /// caller can tell hrms-api. Doesn't commit.
    /// </summary>
    public async Task<bool> AdvanceAsync(AccountSubscription subscription, DateTime nowUtc, CancellationToken token = default)
    {
        var changed = false;
        foreach (var (addOn, from, to) in StorageAddOnPricing.ApplyDueReductions(subscription, nowUtc))
        {
            if (to == 0) Context.StorageAddOns.Remove(addOn);
            await _events.AddAsync(subscription, BillingEventTypes.StorageReductionApplied,
                new { addOn.Id, from, to }, actor: "system", token: token);
            changed = true;
        }

        if (subscription.StorageSubscription is { } storage)
        {
            var hasPaidBlocks = PaidBlocks(subscription).Any();
            foreach (var transition in _states.Advance(storage, nowUtc, hasPaidBlocks, _options))
            {
                await _events.AddAsync(subscription, transition.EventType,
                    new { from = transition.From.ToString(), to = transition.To.ToString(), storage.CurrentPeriodEndUtc, storage.GraceEndsAtUtc },
                    actor: "system", token: token);
                Touch(storage);
                changed = true;
            }
        }

        if (changed)
            await _notifier.PublishAsync(subscription.OwnerUserId, BillingEventTypes.StorageLapsed, token);
        return changed;
    }

    // ── Voids (platform) ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An operator voids a storage payment (never deleted). A voided renewal that defined the running
    /// storage period hands it back: the period falls back to the last live storage payment's, and the
    /// dates decide the status from there. A voided purchase leaves the blocks -- remove them separately
    /// if they shouldn't stay.
    /// </summary>
    public async Task<StorageAddOnPayment> VoidPaymentAsync(
        StorageAddOnPayment payment, string reason, VoidKind kind, string? actor, CancellationToken token = default)
    {
        if (payment.VoidedAtUtc != null) throw new SubscriptionRuleException("This payment is already void.");
        var subscription = await Context.AccountSubscriptions
            .Include(s => s.StorageAddOns)
            .Include(s => s.StorageSubscription)
            .FirstAsync(s => s.Id == payment.SubscriptionId, token);
        var now = Now;
        payment.VoidedAtUtc = now;
        payment.VoidedBy = actor;
        payment.VoidReason = reason;
        payment.VoidKind = kind;

        var storage = subscription.StorageSubscription;
        var periodReverted = false;
        if (storage != null && payment.Kind == StorageAddOnPaymentKind.Renewal && storage.CurrentPeriodEndUtc == payment.PeriodEndUtc)
        {
            var previous = await Context.StorageAddOnPayments.AsNoTracking()
                .Where(p => p.SubscriptionId == subscription.Id && p.Id != payment.Id && p.VoidedAtUtc == null
                    && p.PeriodEndUtc < payment.PeriodEndUtc)
                .OrderByDescending(p => p.PeriodEndUtc)
                .FirstOrDefaultAsync(token);
            // Back to where the voided renewal took over: the previous paid period's end (or, with none
            // left, the renewal's own start -- the old period's end).
            var end = previous?.PeriodEndUtc ?? payment.PeriodStartUtc;
            storage.CurrentPeriodEndUtc = end;
            storage.CurrentPeriodStartUtc = previous?.Kind == StorageAddOnPaymentKind.Renewal
                ? previous.PeriodStartUtc
                : AccountSubscriptionService.AddCycle(end, storage.BillingCycle, -1);
            storage.Status = StorageSubscriptionStatus.Active;
            storage.GraceEndsAtUtc = null;
            _states.Advance(storage, now, PaidBlocks(subscription).Any(), _options);
            Touch(storage);
            periodReverted = true;
        }

        await _events.AddAsync(subscription, BillingEventTypes.StoragePaymentVoided,
            new { payment.Id, payment.Amount, paymentKind = payment.Kind.ToString(), reason, voidKind = kind.ToString(), periodReverted, status = storage?.Status.ToString() },
            actor, token: token);
        await _notifier.PublishAsync(subscription.OwnerUserId, BillingEventTypes.StoragePaymentVoided, token);
        return payment;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────

    private async Task<AccountSubscription> RequireSubscriptionAsync(Guid ownerUserId, CancellationToken token) =>
        await Context.AccountSubscriptions
            .Include(s => s.Plan)
            .Include(s => s.StorageAddOns)
            .Include(s => s.StorageSubscription)
            .FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId, token)
        ?? throw new KeyNotFoundException("This account has no subscription.");

    public static string CycleLabel(BillingCycle cycle) => cycle == BillingCycle.Annual ? "annual" : "monthly";

    private static void Touch(StorageSubscription storage) => storage.ConcurrencyStamp = Guid.NewGuid();
}
