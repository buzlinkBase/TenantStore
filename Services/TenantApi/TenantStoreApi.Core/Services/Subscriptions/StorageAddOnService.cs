using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>The money rules for storage blocks, free of any I/O so they are easy to test.</summary>
public static class StorageAddOnPricing
{
    /// <summary>One block's price for the account's billing cycle: the monthly price, or the annual price
    /// (which the operator sets, so an annual account can be given a discount).</summary>
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
    /// the paid period already counts.</summary>
    public static int BlocksAtRenewal(StorageAddOn addOn, DateTime? periodEndUtc) =>
        addOn.PendingBlocks is { } pending && addOn.PendingEffectiveAtUtc is { } at && periodEndUtc is { } end && at <= end
            ? pending
            : addOn.Blocks;

    /// <summary>What the paid add-ons cost for one more billing period (complimentary ones are free).</summary>
    public static decimal RenewalAmount(AccountSubscription subscription, BillingCycle cycle) =>
        subscription.StorageAddOns
            .Where(a => a.DeletedAt == null && !a.IsComplimentary)
            .Sum(a => BlocksAtRenewal(a, subscription.CurrentPeriodEndUtc) * UnitPrice(a, cycle));

    /// <summary>
    /// The charge for blocks bought mid-period: their price for the whole period (<paramref name="cycleUnitPrice"/>
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

/// <summary>What buying blocks now would cost: the prorated charge, and the new recurring total.</summary>
public sealed record StorageQuote(
    string OfferingCode,
    string OfferingName,
    int Blocks,
    int BlockMb,
    BillingCycle BillingCycle,
    /// <summary>One block's price for the account's billing cycle.</summary>
    decimal UnitPrice,
    decimal AmountDue,
    string Currency,
    bool PricesIncludeVat,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    int DaysRemaining,
    /// <summary>All paid add-ons per billing cycle after this purchase.</summary>
    decimal NewAddOnsTotal,
    int NewLimitMb);

public sealed record StorageAddOnView(
    Guid Id,
    string OfferingCode,
    string OfferingName,
    int Blocks,
    int BlockMb,
    /// <summary>One block's price for the account's billing cycle.</summary>
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

/// <summary>An account's storage: the plan's capacity, the blocks on top, what all its companies use
/// together, and what it costs. Sizes are in MB.</summary>
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
    /// <summary>What the account is billed per period, in its cycle.</summary>
    public BillingCycle BillingCycle { get; init; }
    public decimal PlanPrice { get; init; }
    public decimal AddOnsTotal { get; init; }
    public List<StorageAddOnView> AddOns { get; init; } = [];
    /// <summary>The offerings this account may buy.</summary>
    public List<StorageOfferingView> Offerings { get; init; } = [];
    public List<StorageCompanyUsage> Companies { get; init; } = [];
    /// <summary>Blocks can be bought only on a paid, running plan.</summary>
    public bool CanPurchase { get; init; }
    /// <summary>The limit once scheduled reductions apply at the period end.</summary>
    public int? ProjectedLimitMb { get; init; }
    /// <summary>Usage already exceeds <see cref="ProjectedLimitMb"/>: free space before the renewal or uploads will be blocked.</summary>
    public bool OverProjectedLimit { get; init; }
}

/// <summary>
/// Storage blocks an account holds: buying (settled by checkout), the platform's free grants,
/// scheduled reductions, and the summary the billing screen shows. Storage is one pool for the
/// account -- every company it owns draws on it. Doesn't commit.
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
    private readonly TimeProvider _clock;

    public StorageAddOnService(
        IUnitOfWorkService uow,
        AccountSubscriptionService subscriptions,
        StorageOfferingCatalogService catalog,
        EntitlementResolverService resolver,
        SubscriptionUsageService usage,
        BillingEventService events,
        SubscriptionChangeNotifierService notifier,
        TimeProvider clock)
    {
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

    private static bool IsPaidAndRunning(AccountSubscription s, DateTime now) =>
        s.SubStatus == AccountSubscriptionStatus.Active && s.CurrentPeriodEndUtc > now;

    // ── Quote and purchase ───────────────────────────────────────────────────────────────────

    /// <summary>The prorated price of <paramref name="blocks"/> more blocks for the rest of the paid period.</summary>
    public async Task<StorageQuote> QuoteAsync(Guid ownerUserId, string offeringCode, int blocks, CancellationToken token = default)
    {
        if (blocks < 1) throw new SubscriptionRuleException("Choose at least one block.");
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var now = Now;
        if (!IsPaidAndRunning(subscription, now))
            throw new SubscriptionRuleException("Storage can be added once your plan is paid and running.");
        var offering = await _catalog.RequireVisibleAsync(offeringCode, ownerUserId, token);

        var cycle = subscription.BillingCycle;
        var periodEnd = subscription.CurrentPeriodEndUtc!.Value;
        var periodStart = subscription.CurrentPeriodStartUtc ?? (cycle == BillingCycle.Annual ? periodEnd.AddYears(-1) : periodEnd.AddMonths(-1));
        var unitPrice = StorageAddOnPricing.UnitPrice(offering, cycle);
        var amount = StorageAddOnPricing.Prorate(unitPrice, blocks, periodStart, periodEnd, now);

        var currentPaid = subscription.StorageAddOns
            .Where(a => a.DeletedAt == null && !a.IsComplimentary)
            .Sum(a => a.Blocks * StorageAddOnPricing.UnitPrice(a, cycle));
        var limit = _resolver.ResolveAgainstPlan(subscription, subscription.Plan, Guid.Empty).LimitOf(EntitlementKeys.StorageMaxMb);

        return new StorageQuote(
            offering.Code, offering.Name, blocks, offering.BlockMb, cycle, unitPrice, amount, offering.Currency,
            offering.PricesIncludeVat, periodStart, periodEnd, (int)Math.Ceiling((periodEnd - now).TotalDays),
            currentPaid + unitPrice * blocks,
            limit is { } current ? (int)Math.Min(int.MaxValue, (long)current + (long)blocks * offering.BlockMb) : -1);
    }

    /// <summary>
    /// Adds blocks the customer paid for. Called by checkout settlement with exactly what was
    /// quoted, so a catalog edit between click and payment doesn't change what was bought. The new
    /// ceiling applies at once: a SubscriptionChanged goes out when this commits.
    /// </summary>
    public async Task<StorageAddOnPayment> ActivateAsync(
        Guid ownerUserId, string offeringCode, int blocks, decimal amount,
        PaymentMethod method, string? reference, string? actor, CancellationToken token = default)
    {
        if (blocks < 1) throw new SubscriptionRuleException("Choose at least one block.");
        if (amount < 0) throw new SubscriptionRuleException("Amount can't be negative.");
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var offering = await _catalog.RequireAsync(offeringCode, token);
        var now = Now;
        var periodEnd = subscription.CurrentPeriodEndUtc ?? now;

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
            AddOnId = addOn.Id,
            Blocks = blocks,
            Amount = amount,
            Currency = offering.Currency,
            Method = method,
            Reference = reference,
            PaidAtUtc = now,
            PeriodStartUtc = now,
            PeriodEndUtc = periodEnd,
            RecordedBy = actor,
        };
        await Context.StorageAddOnPayments.AddAsync(payment, token);
        await _events.AddAsync(subscription, BillingEventTypes.StorageAddOnPurchased,
            new { offering = offering.Code, blocks, amount, totalBlocks = addOn.Blocks }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageAddOnPurchased, token);
        return payment;
    }

    // ── Platform grants ──────────────────────────────────────────────────────────────────────

    /// <summary>Gives an account free blocks (a platform decision, e.g. a goodwill gesture or a contract term).
    /// Works on any status: it costs nothing and follows the plan.</summary>
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

    /// <summary>Schedules fewer blocks (0 cancels them) from the end of the paid period. Until then
    /// the capacity -- and the charge -- stay as they are.</summary>
    public async Task<StorageAddOn> RequestReductionAsync(
        Guid ownerUserId, Guid addOnId, int newBlocks, string? actor, CancellationToken token = default)
    {
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var addOn = subscription.StorageAddOns.FirstOrDefault(a => a.Id == addOnId && a.DeletedAt == null)
            ?? throw new KeyNotFoundException("Storage add-on not found.");
        if (newBlocks < 0 || newBlocks >= addOn.Blocks)
            throw new SubscriptionRuleException($"Choose fewer than the {addOn.Blocks} block(s) you hold now.");
        if (subscription.CurrentPeriodEndUtc is not { } periodEnd || subscription.SubStatus != AccountSubscriptionStatus.Active)
            throw new SubscriptionRuleException("There's no paid period to schedule the reduction for.");

        addOn.PendingBlocks = newBlocks;
        addOn.PendingEffectiveAtUtc = periodEnd;
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
        var cycle = subscription.BillingCycle;

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
        var projectedAddOnMb = live.Sum(a => (long)StorageAddOnPricing.BlocksAtRenewal(a, subscription.CurrentPeriodEndUtc) * a.BlockMb);
        int? projected = baseMb is { } b ? (int)Math.Min(int.MaxValue, b + projectedAddOnMb) : null;
        var offerings = (await _catalog.GetVisibleAsync(ownerUserId, token)).Select(StorageOfferingCatalogService.ToView).ToList();

        return new StorageSummary
        {
            BaseMb = baseMb,
            AddOnMb = (int)Math.Min(int.MaxValue, live.Sum(a => (long)StorageAddOnPricing.BlocksAt(a, now) * a.BlockMb)),
            LimitMb = limit,
            UsedMb = used,
            AvailableMb = limit is { } l && used is { } u ? Math.Max(0, l - u) : null,
            PercentUsed = used is { } usedForPercent ? StorageSizing.PercentUsed(usedForPercent, limit) : null,
            Level = used is { } usedForLevel ? StorageSizing.LevelOf(usedForLevel, limit) : StorageLevel.Normal,
            Currency = subscription.Plan.Currency,
            PlanName = subscription.Plan.Name,
            BillingCycle = cycle,
            PlanPrice = cycle == BillingCycle.Annual
                ? subscription.LockedAnnualPrice ?? subscription.Plan.AnnualPrice
                : subscription.LockedMonthlyPrice ?? subscription.Plan.MonthlyPrice,
            AddOnsTotal = live.Where(a => !a.IsComplimentary).Sum(a => a.Blocks * StorageAddOnPricing.UnitPrice(a, cycle)),
            AddOns = live.Select(a =>
            {
                allOfferings.TryGetValue(a.OfferingId, out var offering);
                var unit = StorageAddOnPricing.UnitPrice(a, cycle);
                return new StorageAddOnView(
                    a.Id, offering?.Code ?? string.Empty, offering?.Name ?? string.Empty,
                    a.Blocks, a.BlockMb, unit, a.IsComplimentary ? 0 : a.Blocks * unit, a.IsComplimentary,
                    a.PendingBlocks, a.PendingEffectiveAtUtc, subscription.CurrentPeriodEndUtc);
            }).ToList(),
            Offerings = offerings,
            Companies = companies,
            CanPurchase = IsPaidAndRunning(subscription, now),
            ProjectedLimitMb = projected,
            OverProjectedLimit = projected is { } p && used is { } usedNow && usedNow > p,
        };
    }
}
