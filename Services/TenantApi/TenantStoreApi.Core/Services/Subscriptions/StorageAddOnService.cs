using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>The money rules for storage blocks, free of any I/O so they are easy to test.</summary>
public static class StorageAddOnPricing
{
    /// <summary>One block's price for the storage billing cycle: the monthly price, or the annual price
    /// (which the operator sets, so annual storage can be given a discount).</summary>
    public static decimal UnitPrice(StorageAddOn addOn, BillingCycle cycle) =>
        cycle == BillingCycle.Annual ? addOn.UnitAnnualPrice : addOn.UnitPrice;

    public static decimal UnitPrice(StorageAddOnOffering offering, BillingCycle cycle) =>
        cycle == BillingCycle.Annual ? offering.AnnualPrice : offering.MonthlyPrice;

    /// <summary>Blocks held at <paramref name="nowUtc"/>: a scheduled reduction counts from its date on,
    /// even before the hourly job has made it permanent.</summary>
    public static int BlocksAt(StorageAddOn addOn, DateTime nowUtc) =>
        addOn.PendingBlocks is { } pending && addOn.PendingEffectiveAtUtc is { } at && at <= nowUtc ? pending : addOn.Blocks;

    /// <summary>Makes every scheduled reduction whose date has passed permanent. Returns what changed;
    /// the caller records the events (the lifecycle job does).</summary>
    public static List<(StorageAddOn AddOn, int From, int To)> ApplyDueReductions(AccountSubscription subscription, DateTime nowUtc)
    {
        var applied = new List<(StorageAddOn, int, int)>();
        foreach (var addOn in subscription.StorageAddOns.Where(a => a.DeletedAt == null
                     && a.PendingBlocks != null && a.PendingEffectiveAtUtc != null && a.PendingEffectiveAtUtc <= nowUtc))
        {
            var from = addOn.Blocks;
            var to = addOn.PendingBlocks!.Value;
            addOn.Blocks = to;
            addOn.PendingBlocks = null;
            addOn.PendingEffectiveAtUtc = null;
            applied.Add((addOn, from, to));
        }
        return applied;
    }

    /// <summary>Blocks the add-on will hold at the next renewal: a reduction scheduled for the end of
    /// the paid storage period already counts.</summary>
    public static int BlocksAtRenewal(StorageAddOn addOn, DateTime? periodEndUtc) =>
        addOn.PendingBlocks is { } pending && addOn.PendingEffectiveAtUtc is { } at && periodEndUtc is { } end && at <= end
            ? pending
            : addOn.Blocks;

    /// <summary>What the paid add-ons cost for one more storage period on <paramref name="cycle"/> (complimentary ones are free).</summary>
    public static decimal RenewalAmount(AccountSubscription subscription, BillingCycle cycle) =>
        subscription.StorageAddOns
            .Where(a => a.DeletedAt == null && !a.IsComplimentary)
            .Sum(a => BlocksAtRenewal(a, subscription.StorageSubscription?.CurrentPeriodEndUtc) * UnitPrice(a, cycle));

    /// <summary>The storage the blocks add at <paramref name="nowUtc"/>: free grants always, paid blocks
    /// while the storage period is paid or in its grace.</summary>
    public static long CountedMb(IEnumerable<StorageAddOn> addOns, StorageSubscription? storage, DateTime nowUtc)
    {
        var paidCount = StorageSubscriptionStateMachine.PaidBlocksCount(storage, nowUtc);
        return addOns
            .Where(a => a.DeletedAt == null && (a.IsComplimentary || paidCount))
            .Sum(a => (long)BlocksAt(a, nowUtc) * a.BlockMb);
    }

    /// <summary>
    /// The charge for blocks bought mid storage period: their price for the whole period (<paramref name="cycleUnitPrice"/>
    /// is already the monthly or the annual one), for the part of it still to run. The period's length
    /// comes from its own start and end, so an odd-length period still prorates to exactly 0..1 of the
    /// full price.
    /// </summary>
    public static decimal Prorate(decimal cycleUnitPrice, int blocks, DateTime periodStartUtc, DateTime periodEndUtc, DateTime nowUtc)
    {
        var total = (periodEndUtc - periodStartUtc).TotalSeconds;
        if (total <= 0) return 0;
        var remaining = Math.Clamp((periodEndUtc - nowUtc).TotalSeconds, 0, total);
        return Math.Round(cycleUnitPrice * blocks * (decimal)(remaining / total), 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>What buying blocks now would cost: the prorated charge (or a full period when it starts the
/// storage period), and the new recurring storage total.</summary>
public sealed record StorageQuote(
    string OfferingCode,
    string OfferingName,
    int Blocks,
    int BlockMb,
    /// <summary>The storage cycle: the running one, or the one chosen for a new storage period.</summary>
    BillingCycle BillingCycle,
    /// <summary>One block's price for the storage cycle.</summary>
    decimal UnitPrice,
    decimal AmountDue,
    string Currency,
    bool PricesIncludeVat,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    int DaysRemaining,
    /// <summary>All paid add-ons per storage cycle after this purchase.</summary>
    decimal NewAddOnsTotal,
    int NewLimitMb,
    /// <summary>No storage period was running: this purchase starts one, at the full period's price.</summary>
    bool StartsNewPeriod = false);

public sealed record StorageAddOnView(
    Guid Id,
    string OfferingCode,
    string OfferingName,
    int Blocks,
    int BlockMb,
    /// <summary>One block's price for the storage cycle.</summary>
    decimal UnitPrice,
    decimal CycleTotal,
    bool IsComplimentary,
    int? PendingBlocks,
    DateTime? PendingEffectiveAtUtc,
    DateTime? RenewsAtUtc);

/// <summary>One company's share of the account's storage.</summary>
public sealed record StorageCompanyUsage(Guid TenantId, string CompanyName, int? UsedMb);

/// <summary>Just capacity and usage -- no prices -- for banners shown to admins, who don't hold billing rights.</summary>
public sealed record StorageStatus(
    int? LimitMb, int? UsedMb, int? AvailableMb, int? PercentUsed, StorageLevel Level, bool CanPurchase)
{
    public static StorageStatus From(StorageSummary s) =>
        new(s.LimitMb, s.UsedMb, s.AvailableMb, s.PercentUsed, s.Level, s.CanPurchase);
}

/// <summary>What paying storage now would buy (see <see cref="StorageSubscriptionService.GetRenewalDueAsync"/>).</summary>
public sealed record StorageRenewalDueView(
    BillingCycle BillingCycle,
    decimal Amount,
    string Currency,
    StorageDueReason Reason,
    int Blocks,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    IReadOnlyList<PaymentLineItemSnapshot> LineItems)
{
    public static StorageRenewalDueView From(StorageRenewalDue d) =>
        new(d.BillingCycle, d.Amount, d.Currency, d.Reason, d.Blocks, d.PeriodStartUtc, d.PeriodEndUtc, d.LineItems);
}

/// <summary>An account's storage: the plan's capacity, the blocks on top, what all its companies use
/// together, and what the blocks cost on their own billing cycle. Sizes are in MB.</summary>
public sealed record StorageSummary
{
    /// <summary>Plan storage; null when unlimited.</summary>
    public int? BaseMb { get; init; }
    public int AddOnMb { get; init; }
    /// <summary>Base + blocks; null when unlimited.</summary>
    public int? LimitMb { get; init; }
    /// <summary>All companies together. Null when any of them couldn't be measured -- unknown, not zero.</summary>
    public int? UsedMb { get; init; }
    public int? AvailableMb { get; init; }
    public int? PercentUsed { get; init; }
    public StorageLevel Level { get; init; }
    public string Currency { get; init; } = "PHP";
    public string PlanName { get; init; } = string.Empty;
    /// <summary>The storage billing cycle -- independent of the plan's. Before any storage is bought, the
    /// plan's cycle (the default offered for the first purchase).</summary>
    public BillingCycle BillingCycle { get; init; }
    /// <summary>The cycle the next storage renewal switches to.</summary>
    public BillingCycle? PendingBillingCycle { get; init; }
    /// <summary>The storage period's standing as the dates say now; null before any storage is bought.</summary>
    public StorageSubscriptionStatus? Status { get; init; }
    public DateTime? PeriodStartUtc { get; init; }
    /// <summary>The storage due date.</summary>
    public DateTime? PeriodEndUtc { get; init; }
    /// <summary>Past due: the blocks stop counting after this.</summary>
    public DateTime? GraceEndsAtUtc { get; init; }
    /// <summary>Every paid block is being given up: storage ends at <see cref="PeriodEndUtc"/>.</summary>
    public bool CancelAtPeriodEnd { get; init; }
    /// <summary>What Pay storage would pay for now; null when nothing is due.</summary>
    public StorageRenewalDueView? RenewalDue { get; init; }
    /// <summary>Storage's auto renew, when it's on or being set up.</summary>
    public AutoRenewView? AutoRenew { get; init; }
    /// <summary>The paid blocks per storage cycle.</summary>
    public decimal AddOnsTotal { get; init; }
    public List<StorageAddOnView> AddOns { get; init; } = [];
    /// <summary>The offerings this account may buy.</summary>
    public List<StorageOfferingView> Offerings { get; init; } = [];
    public List<StorageCompanyUsage> Companies { get; init; } = [];
    /// <summary>Blocks can be bought unless the account is suspended by an operator or its storage renewal is unpaid.</summary>
    public bool CanPurchase { get; init; }
    /// <summary>Why <see cref="CanPurchase"/> is false, for the owner.</summary>
    public string? PurchaseBlockedReason { get; init; }
    /// <summary>The limit once scheduled reductions apply at the period end.</summary>
    public int? ProjectedLimitMb { get; init; }
    /// <summary>Usage already exceeds <see cref="ProjectedLimitMb"/>: free space before the renewal or uploads will be blocked.</summary>
    public bool OverProjectedLimit { get; init; }
}

/// <summary>
/// Storage blocks an account holds: buying (settled by checkout), the platform's free grants,
/// scheduled reductions, and the summary the billing screen shows. Storage is one pool for the
/// account -- every company it owns draws on it -- billed on its own cycle and due date
/// (<see cref="StorageSubscriptionService"/>), never with the plan. Doesn't commit.
/// </summary>
public class StorageAddOnService
{
    private readonly IUnitOfWorkService _uow;
    private readonly AccountSubscriptionService _subscriptions;
    private readonly StorageOfferingCatalogService _catalog;
    private readonly EntitlementResolverService _resolver;
    private readonly SubscriptionUsageService _usage;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;
    private readonly StorageSubscriptionService _period;
    private readonly TimeProvider _clock;

    public StorageAddOnService(
        IUnitOfWorkService uow,
        AccountSubscriptionService subscriptions,
        StorageOfferingCatalogService catalog,
        EntitlementResolverService resolver,
        SubscriptionUsageService usage,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        StorageSubscriptionService period,
        TimeProvider clock)
    {
        _period = period;
        _uow = uow;
        _subscriptions = subscriptions;
        _catalog = catalog;
        _resolver = resolver;
        _usage = usage;
        _events = events;
        _notifier = notifier;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    private async Task<AccountSubscription> RequireSubscriptionAsync(Guid ownerUserId, CancellationToken token) =>
        await _subscriptions.FindAsync(ownerUserId, token)
        ?? throw new KeyNotFoundException("This account has no subscription.");

    /// <summary>Why the owner can't buy blocks now, or null when they can.</summary>
    private string? PurchaseBlockedReason(AccountSubscription subscription)
    {
        if (AccountSubscriptionService.IsSuspendedByOperator(subscription))
            return "This account was suspended by Onepunch support. Please contact support.";
        try
        {
            _period.SpanForPurchase(subscription, null);
            return null;
        }
        catch (SubscriptionRuleException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>What a storage payment shows in a payments list, e.g. "2 x 50 GB Storage". The block
    /// may have been removed since, so the offering name can be missing.</summary>
    public static string DescribePayment(int blocks, string? offeringName) =>
        $"{blocks} x {(string.IsNullOrWhiteSpace(offeringName) ? "Storage add-on" : offeringName)}";

    /// <summary>A renewal's line in a payments list, e.g. "Storage renewal (monthly), 2 blocks".</summary>
    public static string DescribeRenewal(int blocks, BillingCycle cycle) =>
        $"Storage renewal ({StorageSubscriptionService.CycleLabel(cycle)}), {blocks} {(blocks == 1 ? "block" : "blocks")}";

    /// <summary>The storage payments of one subscription, newest first, each with a readable description.
    /// Ones voided as an entry error are left out unless asked for, as for plan payments.</summary>
    public async Task<IReadOnlyList<(StorageAddOnPayment Payment, string Description)>> GetPaymentsAsync(
        Guid subscriptionId, CancellationToken token = default, bool includeVoided = false)
    {
        var payments = await Context.StorageAddOnPayments.AsNoTracking()
            .Where(p => p.SubscriptionId == subscriptionId
                && (includeVoided || p.VoidedAtUtc == null || (p.VoidKind != null && p.VoidKind != VoidKind.EntryError)))
            .OrderByDescending(p => p.PaidAtUtc)
            .ToListAsync(token);
        var addOnIds = payments.Select(p => p.AddOnId).Distinct().ToList();
        var names = await Context.StorageAddOns.AsNoTracking()
            .Where(a => addOnIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Offering.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, token);
        return payments
            .Select(p => (p, p.Kind == StorageAddOnPaymentKind.Renewal
                ? DescribeRenewal(p.Blocks, p.BillingCycle)
                : DescribePayment(p.Blocks, names.GetValueOrDefault(p.AddOnId))))
            .ToList();
    }

    // ── Quote and purchase ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The price of <paramref name="blocks"/> more blocks: prorated to the storage due date while a storage
    /// period runs, or a full period on <paramref name="cycle"/> when this purchase starts one. The plan's
    /// status and cycle don't matter -- storage is billed on its own.
    /// </summary>
    public async Task<StorageQuote> QuoteAsync(
        Guid ownerUserId, string offeringCode, int blocks, BillingCycle? cycle = null, CancellationToken token = default)
    {
        if (blocks < 1) throw new SubscriptionRuleException("Choose at least one block.");
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var now = Now;
        if (AccountSubscriptionService.IsSuspendedByOperator(subscription))
            throw new SubscriptionRuleException("This account was suspended by Onepunch support, so storage can't be added. Please contact support.");
        var span = _period.SpanForPurchase(subscription, cycle);
        var offering = await _catalog.RequireVisibleAsync(offeringCode, ownerUserId, token);

        var storageCycle = span.BillingCycle;
        var (periodStart, periodEnd) = (span.PeriodStartUtc, span.PeriodEndUtc);
        var unitPrice = StorageAddOnPricing.UnitPrice(offering, storageCycle);
        var amount = span.StartsNewPeriod
            ? unitPrice * blocks
            : StorageAddOnPricing.Prorate(unitPrice, blocks, periodStart, periodEnd, now);

        var currentPaid = subscription.StorageAddOns
            .Where(a => a.DeletedAt == null && !a.IsComplimentary)
            .Sum(a => a.Blocks * StorageAddOnPricing.UnitPrice(a, storageCycle));
        var limit = _resolver.ResolveAgainstPlan(subscription, subscription.Plan, Guid.Empty).LimitOf(EntitlementKeys.StorageMaxMb);

        return new StorageQuote(
            offering.Code, offering.Name, blocks, offering.BlockMb, storageCycle, unitPrice, amount, offering.Currency,
            offering.PricesIncludeVat, periodStart, periodEnd, (int)Math.Ceiling((periodEnd - now).TotalDays),
            currentPaid + unitPrice * blocks,
            limit is { } current ? (int)Math.Min(int.MaxValue, (long)current + (long)blocks * offering.BlockMb) : -1,
            span.StartsNewPeriod);
    }

    /// <summary>
    /// Adds blocks the customer paid for. Called by checkout settlement with exactly what was
    /// quoted, so a catalog edit between click and payment doesn't change what was bought. Starts the
    /// storage period on <paramref name="cycle"/> when none is running. The new ceiling applies at once:
    /// a SubscriptionChanged goes out when this commits.
    /// </summary>
    public async Task<StorageAddOnPayment> ActivateAsync(
        Guid ownerUserId, string offeringCode, int blocks, decimal amount,
        PaymentMethod method, string? reference, string? actor, BillingCycle? cycle = null, CancellationToken token = default)
    {
        if (blocks < 1) throw new SubscriptionRuleException("Choose at least one block.");
        if (amount < 0) throw new SubscriptionRuleException("Amount can't be negative.");
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var offering = await _catalog.RequireAsync(offeringCode, token);
        var now = Now;
        var span = _period.SpanForPurchase(subscription, cycle);
        await _period.ApplyPurchaseAsync(subscription, span, token);

        var addOn = subscription.StorageAddOns.FirstOrDefault(a => a.DeletedAt == null
            && a.OfferingId == offering.Id && !a.IsComplimentary && a.UnitPrice == offering.MonthlyPrice
            && a.UnitAnnualPrice == offering.AnnualPrice && a.BlockMb == offering.BlockMb);
        if (addOn == null)
        {
            addOn = new StorageAddOn
            {
                Id = Guid.CreateVersion7(),
                SubscriptionId = subscription.Id,
                OfferingId = offering.Id,
                Blocks = 0,
                BlockMb = offering.BlockMb,
                UnitPrice = offering.MonthlyPrice,
                UnitAnnualPrice = offering.AnnualPrice,
                CreatedBy = actor,
            };
            await Context.StorageAddOns.AddAsync(addOn, token);
        }
        addOn.Blocks += blocks;
        // Buying again cancels a scheduled reduction of the same blocks.
        addOn.PendingBlocks = null;
        addOn.PendingEffectiveAtUtc = null;

        var payment = new StorageAddOnPayment
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            Kind = StorageAddOnPaymentKind.Purchase,
            BillingCycle = span.BillingCycle,
            AddOnId = addOn.Id,
            Blocks = blocks,
            Amount = amount,
            Currency = offering.Currency,
            Method = method,
            Reference = reference,
            PaidAtUtc = now,
            PeriodStartUtc = now,
            PeriodEndUtc = span.PeriodEndUtc,
            RecordedBy = actor,
        };
        await Context.StorageAddOnPayments.AddAsync(payment, token);
        await _events.AddAsync(subscription, BillingEventTypes.StorageAddOnPurchased,
            new { offering = offering.Code, blocks, amount, totalBlocks = addOn.Blocks, cycle = span.BillingCycle, span.StartsNewPeriod, periodEndUtc = span.PeriodEndUtc },
            actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageAddOnPurchased, token);
        return payment;
    }

    // ── Platform grants ──────────────────────────────────────────────────────────────────────

    /// <summary>Gives an account free blocks (a platform decision, e.g. a goodwill gesture or a contract term).
    /// Works on any status: it costs nothing, has no due date, and counts until removed.</summary>
    public async Task<StorageAddOn> GrantAsync(
        Guid ownerUserId, string offeringCode, int blocks, string? reason, string? actor, CancellationToken token = default)
    {
        if (blocks < 1) throw new SubscriptionRuleException("Grant at least one block.");
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var offering = await _catalog.RequireAsync(offeringCode, token);

        var addOn = new StorageAddOn
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            OfferingId = offering.Id,
            Blocks = blocks,
            BlockMb = offering.BlockMb,
            UnitPrice = 0,
            UnitAnnualPrice = 0,
            IsComplimentary = true,
            Reason = reason,
            CreatedBy = actor,
        };
        await Context.StorageAddOns.AddAsync(addOn, token);
        await _events.AddAsync(subscription, BillingEventTypes.StorageAddOnGranted,
            new { addOn.Id, offering = offering.Code, blocks, reason }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageAddOnGranted, token);
        return addOn;
    }

    /// <summary>Removes blocks outright (a grant, or paid blocks refunded off-system).</summary>
    public async Task RemoveAsync(Guid ownerUserId, Guid addOnId, string? reason, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var addOn = subscription.StorageAddOns.FirstOrDefault(a => a.Id == addOnId && a.DeletedAt == null)
            ?? throw new KeyNotFoundException("Storage add-on not found.");
        Context.StorageAddOns.Remove(addOn);
        await _events.AddAsync(subscription, BillingEventTypes.StorageAddOnRemoved,
            new { addOn.Id, addOn.Blocks, reason }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageAddOnRemoved, token);
    }

    // ── Reductions (owner) ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Schedules fewer blocks (0 cancels them) from the end of the paid storage period. Until then the
    /// capacity -- and the charge -- stay as they are; giving up every paid block ends the storage period
    /// then, with nothing more to pay. Storage that has lapsed (or is past due) has nothing paid ahead,
    /// so the reduction applies at once.
    /// </summary>
    public async Task<StorageAddOn> RequestReductionAsync(
        Guid ownerUserId, Guid addOnId, int newBlocks, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var addOn = subscription.StorageAddOns.FirstOrDefault(a => a.Id == addOnId && a.DeletedAt == null)
            ?? throw new KeyNotFoundException("Storage add-on not found.");
        if (addOn.IsComplimentary) throw new SubscriptionRuleException("Free storage is removed by Onepunch support.");
        if (newBlocks < 0 || newBlocks >= addOn.Blocks)
            throw new SubscriptionRuleException($"Choose fewer than the {addOn.Blocks} block(s) you hold now.");

        var storage = subscription.StorageSubscription;
        var now = Now;
        if (storage?.CurrentPeriodEndUtc is not { } periodEnd || periodEnd <= now
            || StorageSubscriptionStateMachine.StatusAt(storage, now) != StorageSubscriptionStatus.Active)
        {
            var from = addOn.Blocks;
            addOn.Blocks = newBlocks;
            addOn.PendingBlocks = null;
            addOn.PendingEffectiveAtUtc = null;
            if (newBlocks == 0) Context.StorageAddOns.Remove(addOn);
            await _events.AddAsync(subscription, BillingEventTypes.StorageReductionApplied,
                new { addOn.Id, from, to = newBlocks, immediate = true }, actor, token: token);
            await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageReductionApplied, token);
            return addOn;
        }

        addOn.PendingBlocks = newBlocks;
        addOn.PendingEffectiveAtUtc = periodEnd;
        storage.CancelAtPeriodEnd = StorageSubscriptionService.BlocksToRenew(subscription) == 0;
        await _events.AddAsync(subscription, BillingEventTypes.StorageReductionRequested,
            new { addOn.Id, from = addOn.Blocks, to = newBlocks, effectiveAtUtc = periodEnd }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageReductionRequested, token);
        return addOn;
    }

    public async Task WithdrawReductionAsync(Guid ownerUserId, Guid addOnId, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var addOn = subscription.StorageAddOns.FirstOrDefault(a => a.Id == addOnId && a.DeletedAt == null)
            ?? throw new KeyNotFoundException("Storage add-on not found.");
        if (addOn.PendingBlocks == null) throw new SubscriptionRuleException("There's no reduction to withdraw.");

        addOn.PendingBlocks = null;
        addOn.PendingEffectiveAtUtc = null;
        if (subscription.StorageSubscription is { } storage) storage.CancelAtPeriodEnd = false;
        await _events.AddAsync(subscription, BillingEventTypes.StorageReductionWithdrawn,
            new { addOn.Id }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageReductionWithdrawn, token);
    }

    // ── Summary ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The account's storage as the billing screen shows it. Usage is whatever hrms-api reports
    /// for each company, added up; if any company couldn't be measured the total is unknown
    /// (<see cref="StorageSummary.UsedMb"/> null), never a falsely low number.</summary>
    public async Task<StorageSummary> GetSummaryAsync(Guid ownerUserId, CancellationToken token = default)
    {
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var now = Now;
        var storage = subscription.StorageSubscription;
        var cycle = storage?.BillingCycle ?? subscription.BillingCycle;

        var limit = _resolver.ResolveAgainstPlan(subscription, subscription.Plan, Guid.Empty).LimitOf(EntitlementKeys.StorageMaxMb);
        var planValue = subscription.Plan.Entitlements.FirstOrDefault(e => e.Key == EntitlementKeys.StorageMaxMb)?.Value;
        var baseLimit = planValue == null ? 0 : EntitlementSnapshotBuilder.ParseLimit(planValue);
        int? baseMb = baseLimit == EntitlementKeys.Unlimited ? null : baseLimit;

        var companies = (await _usage.GetCompanyUsageAsync(ownerUserId, token))
            .Select(c => new StorageCompanyUsage(c.TenantId, c.CompanyName, c.Used.GetValueOrDefault(EntitlementKeys.StorageMaxMb)))
            .ToList();
        int? used = companies.Any(c => c.UsedMb is null) ? null : companies.Sum(c => c.UsedMb!.Value);

        var live = subscription.StorageAddOns.Where(a => a.DeletedAt == null).OrderBy(a => a.CreatedAt).ToList();
        var allOfferings = await Context.StorageAddOnOfferings.AsNoTracking().ToDictionaryAsync(o => o.Id, token);
        var paidCount = StorageSubscriptionStateMachine.PaidBlocksCount(storage, now) && storage?.CancelAtPeriodEnd != true;
        // After the storage due date: reductions scheduled for it apply, and giving up every paid block ends them.
        var projectedAddOnMb = live
            .Where(a => a.IsComplimentary || paidCount)
            .Sum(a => (long)StorageAddOnPricing.BlocksAtRenewal(a, storage?.CurrentPeriodEndUtc) * a.BlockMb);
        int? projected = baseMb is { } b ? (int)Math.Min(int.MaxValue, b + projectedAddOnMb) : null;
        var offerings = (await _catalog.GetVisibleAsync(ownerUserId, token)).Select(StorageOfferingCatalogService.ToView).ToList();
        var renewalDue = await _period.GetRenewalDueAsync(subscription, token);
        var blockedReason = PurchaseBlockedReason(subscription);

        return new StorageSummary
        {
            BaseMb = baseMb,
            AddOnMb = (int)Math.Min(int.MaxValue, StorageAddOnPricing.CountedMb(live, storage, now)),
            LimitMb = limit,
            UsedMb = used,
            AvailableMb = limit is { } l && used is { } u ? Math.Max(0, l - u) : null,
            PercentUsed = used is { } usedForPercent ? StorageSizing.PercentUsed(usedForPercent, limit) : null,
            Level = used is { } usedForLevel ? StorageSizing.LevelOf(usedForLevel, limit) : StorageLevel.Normal,
            Currency = subscription.Plan.Currency,
            PlanName = subscription.Plan.Name,
            BillingCycle = cycle,
            PendingBillingCycle = storage?.PendingBillingCycle,
            Status = storage == null ? null : StorageSubscriptionStateMachine.StatusAt(storage, now),
            PeriodStartUtc = storage?.CurrentPeriodStartUtc,
            PeriodEndUtc = storage?.CurrentPeriodEndUtc,
            GraceEndsAtUtc = storage?.GraceEndsAtUtc,
            CancelAtPeriodEnd = storage?.CancelAtPeriodEnd ?? false,
            RenewalDue = renewalDue == null ? null : StorageRenewalDueView.From(renewalDue),
            AutoRenew = (await AutoRenewReadModel.LiveAsync(Context, subscription.Id, token)).FirstOrDefault(a => a.Target == AutoRenewTarget.Storage),
            AddOnsTotal = live.Where(a => !a.IsComplimentary).Sum(a => a.Blocks * StorageAddOnPricing.UnitPrice(a, cycle)),
            AddOns = live.Select(a =>
            {
                allOfferings.TryGetValue(a.OfferingId, out var offering);
                var unit = StorageAddOnPricing.UnitPrice(a, cycle);
                return new StorageAddOnView(
                    a.Id, offering?.Code ?? string.Empty, offering?.Name ?? string.Empty,
                    a.Blocks, a.BlockMb, unit, a.IsComplimentary ? 0 : a.Blocks * unit, a.IsComplimentary,
                    a.PendingBlocks, a.PendingEffectiveAtUtc, a.IsComplimentary ? null : storage?.CurrentPeriodEndUtc);
            }).ToList(),
            Offerings = offerings,
            Companies = companies,
            CanPurchase = blockedReason == null,
            PurchaseBlockedReason = blockedReason,
            ProjectedLimitMb = projected,
            OverProjectedLimit = projected is { } p && used is { } usedNow && usedNow > p,
        };
    }
}
