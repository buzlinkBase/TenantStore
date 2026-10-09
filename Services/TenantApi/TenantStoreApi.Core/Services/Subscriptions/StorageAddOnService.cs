using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>The money rules for storage blocks, free of any I/O so they are easy to test.</summary>
public static class StorageAddOnPricing
{
    public static int MonthsIn(BillingCycle cycle) => cycle == BillingCycle.Annual ? 12 : 1;

    /// <summary>Blocks the add-on will hold at the next renewal: a reduction scheduled for the end of
    /// the paid period already counts.</summary>
    public static int BlocksAtRenewal(StorageAddOn addOn, DateTime? periodEndUtc) =>
        addOn.PendingBlocks is { } pending && addOn.PendingEffectiveAtUtc is { } at && periodEndUtc is { } end && at <= end
            ? pending
            : addOn.Blocks;

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

    /// <summary>What the paid add-ons cost for one more billing period (complimentary ones are free).</summary>
    public static decimal RenewalAmount(AccountSubscription subscription, BillingCycle cycle) =>
        subscription.StorageAddOns
            .Where(a => a.DeletedAt == null && !a.IsComplimentary)
            .Sum(a => BlocksAtRenewal(a, subscription.CurrentPeriodEndUtc) * a.UnitPrice)
        * MonthsIn(cycle);

    /// <summary>
    /// The charge for blocks bought mid-period: their price for the whole period, for the part of it
    /// still to run. The period's length comes from its own start and end, so an odd-length period
    /// still prorates to exactly 0..1 of the full price.
    /// </summary>
    public static decimal Prorate(decimal unitPrice, int blocks, BillingCycle cycle, DateTime periodStartUtc, DateTime periodEndUtc, DateTime nowUtc)
    {
        var total = (periodEndUtc - periodStartUtc).TotalSeconds;
        if (total <= 0) return 0;
        var remaining = Math.Clamp((periodEndUtc - nowUtc).TotalSeconds, 0, total);
        var full = unitPrice * blocks * MonthsIn(cycle);
        return Math.Round(full * (decimal)(remaining / total), 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>What buying blocks now would cost: the prorated charge, and the new recurring total.</summary>
public sealed record StorageQuote(
    string OfferingCode,
    string OfferingName,
    int Blocks,
    int BlockMb,
    decimal UnitPrice,
    decimal AmountDue,
    string Currency,
    bool PricesIncludeVat,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    int DaysRemaining,
    /// <summary>The monthly cost of all paid add-ons after this purchase.</summary>
    decimal NewMonthlyAddOnsTotal,
    int NewLimitMb);

public sealed record StorageAddOnView(
    Guid Id,
    string OfferingCode,
    string OfferingName,
    int Blocks,
    int BlockMb,
    decimal UnitPrice,
    decimal MonthlyTotal,
    bool IsComplimentary,
    int? PendingBlocks,
    DateTime? PendingEffectiveAtUtc,
    DateTime? RenewsAtUtc);

/// <summary>One company's storage: base plan capacity, the blocks on top, usage, and what it costs.</summary>
public sealed record StorageSummary
{
    public Guid TenantId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    /// <summary>Base plan storage in MB; null when unlimited.</summary>
    public int? BaseMb { get; init; }
    public int AddOnMb { get; init; }
    /// <summary>Base + blocks; null when unlimited.</summary>
    public int? LimitMb { get; init; }
    /// <summary>Null when it couldn't be measured -- unknown, not zero.</summary>
    public int? UsedMb { get; init; }
    public int? AvailableMb { get; init; }
    public int? PercentUsed { get; init; }
    public StorageLevel Level { get; init; }
    public string Currency { get; init; } = "PHP";
    public string PlanName { get; init; } = string.Empty;
    public decimal PlanMonthlyPrice { get; init; }
    public decimal AddOnsMonthlyTotal { get; init; }
    public List<StorageAddOnView> AddOns { get; init; } = [];
    /// <summary>The offerings this account may buy.</summary>
    public List<StorageOfferingView> Offerings { get; init; } = [];
    /// <summary>Blocks can be bought only on a paid, running plan.</summary>
    public bool CanPurchase { get; init; }
    /// <summary>The limit once scheduled reductions apply at the period end.</summary>
    public int? ProjectedLimitMb { get; init; }
    /// <summary>Usage already exceeds <see cref="ProjectedLimitMb"/>: free space before the renewal or uploads will be blocked.</summary>
    public bool OverProjectedLimit { get; init; }
}

/// <summary>Just capacity and usage -- no prices -- for banners shown to admins, who don't hold billing rights.</summary>
public sealed record StorageStatus(
    int? LimitMb, int? UsedMb, int? AvailableMb, int? PercentUsed, StorageLevel Level, bool CanPurchase)
{
    public static StorageStatus From(StorageSummary s) =>
        new(s.LimitMb, s.UsedMb, s.AvailableMb, s.PercentUsed, s.Level, s.CanPurchase);
}

/// <summary>
/// Storage blocks a company holds: buying (settled by checkout), the platform's free grants,
/// scheduled reductions, and the summary the billing screen shows. Doesn't commit.
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

    private async Task EnsureOwnedAsync(Guid ownerUserId, Guid tenantId, CancellationToken token)
    {
        var owned = await Context.Tenants.AnyAsync(t => t.Id == tenantId && t.UserId == ownerUserId && t.ActivatedAtUtc != null, token);
        if (!owned) throw new SubscriptionRuleException("That company isn't owned by this account.");
    }

    private static bool IsPaidAndRunning(AccountSubscription s, DateTime now) =>
        s.SubStatus == AccountSubscriptionStatus.Active && s.CurrentPeriodEndUtc > now;

    // ── Quote and purchase ───────────────────────────────────────────────────────────────────

    /// <summary>The prorated price of <paramref name="blocks"/> more blocks for the rest of the paid period.</summary>
    public async Task<StorageQuote> QuoteAsync(Guid ownerUserId, Guid tenantId, string offeringCode, int blocks, CancellationToken token = default)
    {
        if (blocks < 1) throw new SubscriptionRuleException("Choose at least one block.");
        await EnsureOwnedAsync(ownerUserId, tenantId, token);
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var now = Now;
        if (!IsPaidAndRunning(subscription, now))
            throw new SubscriptionRuleException("Storage can be added once your plan is paid and running.");
        var offering = await _catalog.RequireVisibleAsync(offeringCode, ownerUserId, token);

        var periodEnd = subscription.CurrentPeriodEndUtc!.Value;
        var periodStart = subscription.CurrentPeriodStartUtc
            ?? (subscription.BillingCycle == BillingCycle.Annual ? periodEnd.AddYears(-1) : periodEnd.AddMonths(-1));
        var amount = StorageAddOnPricing.Prorate(offering.MonthlyPrice, blocks, subscription.BillingCycle, periodStart, periodEnd, now);

        var currentPaid = subscription.StorageAddOns.Where(a => a.DeletedAt == null && !a.IsComplimentary).Sum(a => a.Blocks * a.UnitPrice);
        var snapshot = await _resolver.ResolveActualForTenantAsync(tenantId, token);
        var currentLimit = snapshot.LimitOf(EntitlementKeys.StorageMaxMb);

        return new StorageQuote(
            offering.Code, offering.Name, blocks, offering.BlockMb, offering.MonthlyPrice, amount, offering.Currency,
            offering.PricesIncludeVat, periodStart, periodEnd, (int)Math.Ceiling((periodEnd - now).TotalDays),
            currentPaid + offering.MonthlyPrice * blocks,
            currentLimit is { } limit ? (int)Math.Min(int.MaxValue, (long)limit + (long)blocks * offering.BlockMb) : -1);
    }

    /// <summary>
    /// Adds blocks the customer paid for. Called by checkout settlement with exactly what was
    /// quoted, so a catalog edit between click and payment doesn't change what was bought. The new
    /// ceiling applies at once: a SubscriptionChanged goes out when this commits.
    /// </summary>
    public async Task<StorageAddOnPayment> ActivateAsync(
        Guid ownerUserId, Guid tenantId, string offeringCode, int blocks, decimal amount,
        PaymentMethod method, string? reference, string? actor, CancellationToken token = default)
    {
        if (blocks < 1) throw new SubscriptionRuleException("Choose at least one block.");
        if (amount < 0) throw new SubscriptionRuleException("Amount can't be negative.");
        await EnsureOwnedAsync(ownerUserId, tenantId, token);
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var offering = await _catalog.RequireAsync(offeringCode, token);
        var now = Now;
        var periodEnd = subscription.CurrentPeriodEndUtc ?? now;

        var addOn = subscription.StorageAddOns.FirstOrDefault(a => a.DeletedAt == null && a.TenantId == tenantId
            && a.OfferingId == offering.Id && !a.IsComplimentary && a.UnitPrice == offering.MonthlyPrice && a.BlockMb == offering.BlockMb);
        if (addOn == null)
        {
            addOn = new StorageAddOn
            {
                Id = Guid.CreateVersion7(),
                SubscriptionId = subscription.Id,
                TenantId = tenantId,
                OfferingId = offering.Id,
                Blocks = 0,
                BlockMb = offering.BlockMb,
                UnitPrice = offering.MonthlyPrice,
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
            TenantId = tenantId,
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
            new { tenantId, offering = offering.Code, blocks, amount, totalBlocks = addOn.Blocks }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageAddOnPurchased, token);
        return payment;
    }

    // ── Platform grants ──────────────────────────────────────────────────────────────────────

    public async Task<List<StorageAddOn>> GetForAccountAsync(Guid ownerUserId, CancellationToken token = default)
    {
        var subscription = await _subscriptions.FindAsync(ownerUserId, token);
        return subscription == null
            ? []
            : subscription.StorageAddOns.Where(a => a.DeletedAt == null).OrderBy(a => a.CreatedAt).ToList();
    }

    /// <summary>Gives a company free blocks (a platform decision, e.g. a goodwill gesture or a contract term).
    /// Works on any status: it costs nothing and follows the plan.</summary>
    public async Task<StorageAddOn> GrantAsync(
        Guid ownerUserId, Guid tenantId, string offeringCode, int blocks, string? reason, string? actor, CancellationToken token = default)
    {
        if (blocks < 1) throw new SubscriptionRuleException("Grant at least one block.");
        await EnsureOwnedAsync(ownerUserId, tenantId, token);
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var offering = await _catalog.RequireAsync(offeringCode, token);

        var addOn = new StorageAddOn
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            TenantId = tenantId,
            OfferingId = offering.Id,
            Blocks = blocks,
            BlockMb = offering.BlockMb,
            UnitPrice = 0,
            IsComplimentary = true,
            Reason = reason,
            CreatedBy = actor,
        };
        await Context.StorageAddOns.AddAsync(addOn, token);
        await _events.AddAsync(subscription, BillingEventTypes.StorageAddOnGranted,
            new { addOn.Id, tenantId, offering = offering.Code, blocks, reason }, actor, token: token);
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
            new { addOn.Id, addOn.TenantId, addOn.Blocks, reason }, actor, token: token);
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
            new { addOn.Id, addOn.TenantId, from = addOn.Blocks, to = newBlocks, effectiveAtUtc = periodEnd }, actor, token: token);
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
            new { addOn.Id, addOn.TenantId }, actor, token: token);
        await _notifier.PublishAsync(ownerUserId, BillingEventTypes.StorageReductionWithdrawn, token);
    }

    // ── Summary ──────────────────────────────────────────────────────────────────────────────

    /// <summary>One company's storage as the billing screen shows it. Usage is whatever hrms-api reports;
    /// if it couldn't be measured, <see cref="StorageSummary.UsedMb"/> is null rather than 0.</summary>
    public async Task<StorageSummary> GetSummaryAsync(Guid ownerUserId, Guid tenantId, CancellationToken token = default)
    {
        await EnsureOwnedAsync(ownerUserId, tenantId, token);
        return (await SummariesAsync(ownerUserId, tenantId, token)).FirstOrDefault()
            ?? throw new SubscriptionRuleException("That company isn't owned by this account.");
    }

    /// <summary>Every company the account owns, for the platform console's account view.</summary>
    public Task<List<StorageSummary>> GetAccountSummariesAsync(Guid ownerUserId, CancellationToken token = default) =>
        SummariesAsync(ownerUserId, null, token);

    private async Task<List<StorageSummary>> SummariesAsync(Guid ownerUserId, Guid? onlyTenant, CancellationToken token)
    {
        var subscription = await RequireSubscriptionAsync(ownerUserId, token);
        var now = Now;
        var usage = await _usage.GetCompanyUsageAsync(ownerUserId, token);
        var offerings = (await _catalog.GetVisibleAsync(ownerUserId, token)).Select(StorageOfferingCatalogService.ToView).ToList();

        var allOfferings = await Context.StorageAddOnOfferings.AsNoTracking().ToDictionaryAsync(o => o.Id, token);
        var planValue = subscription.Plan.Entitlements.FirstOrDefault(e => e.Key == EntitlementKeys.StorageMaxMb)?.Value;
        var baseLimit = planValue == null ? 0 : EntitlementSnapshotBuilder.ParseLimit(planValue);
        int? baseMb = baseLimit == EntitlementKeys.Unlimited ? null : baseLimit;

        var summaries = new List<StorageSummary>();
        foreach (var company in usage.Where(c => onlyTenant == null || c.TenantId == onlyTenant))
        {
            var snapshot = await _resolver.ResolveActualForTenantAsync(company.TenantId, token);
            var limit = snapshot.LimitOf(EntitlementKeys.StorageMaxMb);
            int? used = company.Used.GetValueOrDefault(EntitlementKeys.StorageMaxMb);

            var live = subscription.StorageAddOns
                .Where(a => a.DeletedAt == null && a.TenantId == company.TenantId)
                .OrderBy(a => a.CreatedAt)
                .ToList();
            var projectedAddOnMb = live.Sum(a => (long)StorageAddOnPricing.BlocksAtRenewal(a, subscription.CurrentPeriodEndUtc) * a.BlockMb);
            int? projected = baseMb is { } b ? (int)Math.Min(int.MaxValue, b + projectedAddOnMb) : null;

            summaries.Add(new StorageSummary
            {
                TenantId = company.TenantId,
                CompanyName = company.CompanyName,
                BaseMb = baseMb,
                AddOnMb = (int)Math.Min(int.MaxValue, live.Sum(a => (long)StorageAddOnPricing.BlocksAt(a, now) * a.BlockMb)),
                LimitMb = limit,
                UsedMb = used,
                AvailableMb = limit is { } l && used is { } u ? Math.Max(0, l - u) : null,
                PercentUsed = used is { } usedForPercent ? StorageSizing.PercentUsed(usedForPercent, limit) : null,
                Level = used is { } usedForLevel ? StorageSizing.LevelOf(usedForLevel, limit) : StorageLevel.Normal,
                Currency = subscription.Plan.Currency,
                PlanName = subscription.Plan.Name,
                PlanMonthlyPrice = subscription.LockedMonthlyPrice ?? subscription.Plan.MonthlyPrice,
                AddOnsMonthlyTotal = live.Where(a => !a.IsComplimentary).Sum(a => a.Blocks * a.UnitPrice),
                AddOns = live.Select(a =>
                {
                    allOfferings.TryGetValue(a.OfferingId, out var offering);
                    return new StorageAddOnView(
                        a.Id, offering?.Code ?? string.Empty, offering?.Name ?? string.Empty,
                        a.Blocks, a.BlockMb, a.UnitPrice, a.IsComplimentary ? 0 : a.Blocks * a.UnitPrice, a.IsComplimentary,
                        a.PendingBlocks, a.PendingEffectiveAtUtc, subscription.CurrentPeriodEndUtc);
                }).ToList(),
                Offerings = offerings,
                CanPurchase = IsPaidAndRunning(subscription, now),
                ProjectedLimitMb = projected,
                OverProjectedLimit = projected is { } p && used is { } usedNow && usedNow > p,
            });
        }
        return summaries;
    }
}
