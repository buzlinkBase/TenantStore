using System.Text.Json;
using Microsoft.Extensions.Options;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Payments;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Subscriptions;

public sealed record PlanView(
    string Code,
    string Name,
    string Description,
    int SortOrder,
    bool IsRecommended,
    bool IsPublic,
    decimal MonthlyPrice,
    decimal AnnualPrice,
    string Currency,
    bool PricesIncludeVat,
    Dictionary<string, bool> Features,
    Dictionary<string, int> Limits,
    string? SupportTier);

/// <summary>What every member's session needs to lock and unlock the UI: the company's snapshot,
/// plus the lowest plan that includes each feature it lacks (for "Available on Payroll").</summary>
public sealed record EntitlementsView(
    EntitlementSnapshot Entitlements,
    Dictionary<string, string> FeatureAvailableOn);

public sealed record PendingChangeView(
    string PlanCode,
    string PlanName,
    BillingCycle BillingCycle,
    PlanChangeTiming Timing,
    DateTime? EffectiveAtUtc,
    decimal AmountDue,
    /// <summary>An upgrade's credit for the unused days of the running period, already taken off <see cref="AmountDue"/>.</summary>
    decimal CreditAmount = 0m,
    int UnusedDays = 0);

/// <summary>What Pay now would pay for (see AccountSubscriptionService.GetPaymentDueAsync).</summary>
public sealed record PaymentDueView(
    string PlanCode,
    string PlanName,
    BillingCycle BillingCycle,
    decimal Amount,
    PaymentDueReason Reason,
    decimal AddOnsAmount = 0m,
    /// <summary>An upgrade's credit for the unused days of the running period, already taken off <see cref="Amount"/>.</summary>
    decimal CreditAmount = 0m,
    int UnusedDays = 0);

public sealed record CompanyUsageView(
    Guid TenantId,
    string Name,
    Dictionary<string, int?> Used,
    Dictionary<string, int?> Max);

public sealed record PaymentView(
    Guid Id,
    DateTime PaidAtUtc,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    string? Reference,
    string? PlanName,
    BillingCycle BillingCycle,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    /// <summary>"Plan" for a plan period, "Storage" for storage blocks bought mid-period.</summary>
    string Kind = PaymentKinds.Plan,
    /// <summary>Storage payments: what was bought, e.g. "2 x 50 GB Storage".</summary>
    string? Description = null,
    /// <summary>"Paid", or "Refunded" / "Cancelled" for a voided payment the owner still sees.</summary>
    string Status = PaymentStatuses.Paid,
    /// <summary>How much of it has gone back to the payer (a refund sent from the console or made in the gateway).</summary>
    decimal RefundedAmount = 0m,
    /// <summary>Point-in-time plan and storage amounts; null for records made before line-item snapshots.</summary>
    IReadOnlyList<PaymentLineItemSnapshot>? LineItems = null,
    /// <summary>The unused time of the previous plan put towards this period on an upgrade.</summary>
    decimal CreditAmount = 0m);

public static class PaymentStatuses
{
    public const string Paid = "Paid";
    /// <summary>Some of the payment was refunded; it still counts, so the plan period is unchanged.</summary>
    public const string PartiallyRefunded = "PartiallyRefunded";
    public const string Refunded = "Refunded";

    /// <summary>A voided payment shows why it was voided; otherwise how much of it was refunded decides.</summary>
    public static string Of(string? voidKind, decimal amount, decimal refunded) =>
        voidKind != null ? voidKind
        : refunded <= 0m ? Paid
        : refunded >= amount ? Refunded
        : PartiallyRefunded;
}

/// <summary>The kinds of payment a payments list mixes.</summary>
public static class PaymentKinds
{
    public const string Plan = "Plan";
    public const string Storage = "Storage";
}

/// <summary>The billing screen: plan, status, usage against limits, payments.</summary>
public sealed record BillingView
{
    public EntitlementSnapshot Entitlements { get; init; } = new();
    public BillingCycle? BillingCycle { get; init; }
    public DateTime? CurrentPeriodStartUtc { get; init; }
    public DateTime? PurgeAfterUtc { get; init; }
    public int TrialDays { get; init; }
    /// <summary>1 on the trial's first day; null when not trialing.</summary>
    public int? TrialDay { get; init; }
    public bool TrialExtended { get; init; }
    public PendingChangeView? PendingChange { get; init; }
    /// <summary>What the next period costs on the current plan and cycle, after discounts.</summary>
    public decimal? NextAmountDue { get; init; }
    public string Currency { get; init; } = "PHP";
    public int CompaniesUsed { get; init; }
    public int? CompaniesMax { get; init; }
    public List<PaymentView> Payments { get; init; } = [];
    public string PaymentInstructions { get; init; } = string.Empty;
    /// <summary>What the account can pay for now; null when nothing is due.</summary>
    public PaymentDueView? PaymentDue { get; init; }
    /// <summary>Whether Pay now (the payment gateway's hosted page) is offered.</summary>
    public bool OnlinePaymentEnabled { get; init; }
}

/// <summary>
/// Per-company usage against limits, and the plan it points to. Served apart from <see cref="BillingView"/>
/// because counting asks other services (hrms-api, hrms-adms-api), which is slow -- the billing screen
/// shows the plan and payments at once and fills this in when it arrives.
/// </summary>
public sealed record UsageView
{
    public List<CompanyUsageView> Companies { get; init; } = [];
    /// <summary>The cheapest plan covering everything the account has used and fits its usage --
    /// what the plan selection screen pre-selects.</summary>
    public string? RecommendedPlanCode { get; init; }
}

/// <summary>Builds the subscription read models the controllers return.</summary>
public class SubscriptionViewService
{
    private readonly AccountSubscriptionService _subscriptions;
    private readonly EntitlementResolverService _resolver;
    private readonly SubscriptionUsageService _usage;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;
    private readonly IPaymentGateway _gateway;
    private readonly StorageAddOnService _storage;

    public SubscriptionViewService(
        AccountSubscriptionService subscriptions,
        EntitlementResolverService resolver,
        SubscriptionUsageService usage,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock,
        IPaymentGateway gateway,
        StorageAddOnService storage)
    {
        _storage = storage;
        _subscriptions = subscriptions;
        _resolver = resolver;
        _usage = usage;
        _options = options.Value;
        _clock = clock;
        _gateway = gateway;
    }

    public static PlanView ToView(Plan plan)
    {
        var values = plan.Entitlements.ToDictionary(e => e.Key, e => e.Value);
        return new PlanView(
            plan.Code, plan.Name, plan.Description, plan.SortOrder, plan.IsRecommended, plan.IsPublic,
            plan.MonthlyPrice, plan.AnnualPrice, plan.Currency, plan.PricesIncludeVat,
            EntitlementKeys.Features.ToDictionary(k => k, k => values.TryGetValue(k, out var v) && EntitlementSnapshotBuilder.ParseBool(v)),
            EntitlementKeys.Limits.ToDictionary(k => k, k => values.TryGetValue(k, out var v) ? EntitlementSnapshotBuilder.ParseLimit(v) : 0),
            values.GetValueOrDefault(EntitlementKeys.SupportTier));
    }

    /// <summary>The plans this owner may pick: the public catalog plus any private plan offered to them.</summary>
    public async Task<List<PlanView>> GetVisiblePlansAsync(Guid ownerUserId, CancellationToken token = default) =>
        (await _subscriptions.GetVisiblePlansAsync(ownerUserId, token)).Select(ToView).ToList();

    /// <param name="isBillingUser">Whether the caller is the owner or a billing admin: only they are
    /// told why an operator stopped the account; other members just learn that it is stopped.</param>
    public async Task<EntitlementsView> GetEntitlementsAsync(Guid tenantId, bool isBillingUser, CancellationToken token = default)
    {
        var snapshot = await _resolver.ResolveForTenantAsync(tenantId, token);
        snapshot = snapshot with { Restriction = snapshot.Restriction?.ForViewer(isBillingUser) };
        var plans = await _subscriptions.GetVisiblePlansAsync(snapshot.OwnerUserId, token);
        var availableOn = EntitlementKeys.Features
            .Select(key => (key, plan: plans.FirstOrDefault(p => ToView(p).Features[key])))
            .Where(x => x.plan != null)
            .ToDictionary(x => x.key, x => x.plan!.Name);
        return new EntitlementsView(snapshot, availableOn);
    }

    /// <summary>The billing view as seen from one of the account's companies.</summary>
    public async Task<BillingView> GetBillingForTenantAsync(Guid tenantId, CancellationToken token = default)
    {
        var snapshot = await _resolver.ResolveActualForTenantAsync(tenantId, token);
        return await BuildAsync(snapshot, token);
    }

    /// <summary>Usage per company and the recommended plan, as seen from one of the account's companies.</summary>
    public async Task<UsageView> GetUsageForTenantAsync(Guid tenantId, CancellationToken token = default)
    {
        var snapshot = await _resolver.ResolveActualForTenantAsync(tenantId, token);
        if (snapshot.OwnerUserId is not { } owner) return new UsageView();

        var subscription = await _subscriptions.FindAsync(owner, token);
        if (subscription == null) return new UsageView();

        var plans = await _subscriptions.GetVisiblePlansAsync(owner, token);
        var usage = await _usage.GetCompanyUsageAsync(owner, token);

        return new UsageView
        {
            Companies = usage.Select(company =>
            {
                var companySnapshot = _resolver.ResolveAgainstPlan(subscription, subscription.Plan, company.TenantId);
                var limits = company.Limits.ToDictionary(kv => kv.Key, kv => kv.Value);
                return new CompanyUsageView(
                    company.TenantId,
                    company.CompanyName,
                    limits,
                    limits.Keys.ToDictionary(key => key, key => companySnapshot.LimitOf(key)));
            }).ToList(),
            RecommendedPlanCode = Recommend(subscription, plans, usage),
        };
    }

    /// <summary>The billing view of an account (platform console), as of its account-level entitlements.</summary>
    public async Task<BillingView> GetBillingForAccountAsync(Guid ownerUserId, CancellationToken token = default)
    {
        var subscription = await _subscriptions.FindAsync(ownerUserId, token)
            ?? throw new KeyNotFoundException("This account has no subscription.");
        var snapshot = _resolver.ResolveAgainstPlan(subscription, subscription.Plan, Guid.Empty);
        return await BuildAsync(snapshot, token);
    }

    private async Task<BillingView> BuildAsync(EntitlementSnapshot snapshot, CancellationToken token)
    {
        var view = new BillingView
        {
            Entitlements = snapshot,
            TrialDays = _options.TrialDays,
            PaymentInstructions = _options.PaymentInstructions,
        };
        if (snapshot.OwnerUserId is not { } owner) return view;

        var subscription = await _subscriptions.FindAsync(owner, token);
        if (subscription == null) return view;

        var now = _clock.GetUtcNow().UtcDateTime;
        var plans = await _subscriptions.GetVisiblePlansAsync(owner, token);

        var due = await _subscriptions.GetPaymentDueAsync(subscription, token);

        PendingChangeView? pending = null;
        if (subscription.PendingPlanId is { } pendingPlanId
            && await _subscriptions.FindPlanAsync(pendingPlanId, token) is { } pendingPlan)
        {
            var cycle = subscription.PendingBillingCycle ?? subscription.BillingCycle;
            var timing = subscription.PendingChangeTiming ?? PlanChangeTiming.OnPayment;
            // An upgrade waiting for payment costs what Pay now charges: its credit already taken off.
            pending = timing == PlanChangeTiming.OnPayment && due is { Reason: PaymentDueReason.PlanChange } upgrade
                ? new PendingChangeView(
                    pendingPlan.Code, pendingPlan.Name, cycle, timing, subscription.PendingChangeEffectiveAtUtc,
                    upgrade.Amount, upgrade.CreditAmount, upgrade.UnusedDays)
                : new PendingChangeView(
                    pendingPlan.Code, pendingPlan.Name, cycle, timing, subscription.PendingChangeEffectiveAtUtc,
                    await _subscriptions.PriceForAsync(subscription, pendingPlan, cycle, token));
        }

        var payments = await _subscriptions.GetPaymentsAsync(subscription.Id, token: token);
        var planNames = plans.ToDictionary(p => p.Id, p => p.Name);
        planNames.TryAdd(subscription.PlanId, subscription.Plan.Name);
        var storagePayments = await _storage.GetPaymentsAsync(subscription.Id, token);
        // Refunds -- sent from the console or made in the gateway -- show here as soon as they happen, voided or not.
        var gatewayIssues = await _subscriptions.LoadGatewayIssuesAsync(owner, token);
        var paymentViews = payments
            .Select(p =>
            {
                var refunded = gatewayIssues.RefundedOf(p.Reference);
                return new PaymentView(
                    p.Id, p.PaidAtUtc, p.Amount, p.Currency, p.Method, p.Reference,
                    planNames.GetValueOrDefault(p.PlanId), p.BillingCycle, p.PeriodStartUtc, p.PeriodEndUtc,
                    Status: PaymentStatuses.Of(p.VoidedAtUtc == null ? null : p.VoidKind.ToString(), p.Amount, refunded),
                    RefundedAmount: refunded,
                    LineItems: p.LineItemsJson == null
                        ? null
                        : JsonSerializer.Deserialize<List<PaymentLineItemSnapshot>>(p.LineItemsJson),
                    CreditAmount: p.CreditAmount);
            })
            .Concat(storagePayments.Select(s =>
            {
                var refunded = gatewayIssues.RefundedOf(s.Payment.Reference);
                return new PaymentView(
                    s.Payment.Id, s.Payment.PaidAtUtc, s.Payment.Amount, s.Payment.Currency, s.Payment.Method,
                    s.Payment.Reference, subscription.Plan.Name, subscription.BillingCycle,
                    s.Payment.PeriodStartUtc, s.Payment.PeriodEndUtc, PaymentKinds.Storage, s.Description,
                    Status: PaymentStatuses.Of(null, s.Payment.Amount, refunded), RefundedAmount: refunded);
            }))
            .OrderByDescending(p => p.PaidAtUtc)
            .ToList();

        return view with
        {
            BillingCycle = subscription.BillingCycle,
            CurrentPeriodStartUtc = subscription.CurrentPeriodStartUtc,
            PurgeAfterUtc = subscription.PurgeAfterUtc,
            TrialDay = subscription.SubStatus == AccountSubscriptionStatus.Trialing
                ? SubscriptionReminderSchedule.TrialDay(subscription, now, _options)
                : null,
            TrialExtended = subscription.TrialExtended,
            PendingChange = pending,
            NextAmountDue = await _subscriptions.PriceForAsync(subscription, subscription.Plan, subscription.BillingCycle, token),
            Currency = subscription.Plan.Currency,
            CompaniesUsed = await _usage.CountOwnedCompaniesAsync(owner, token),
            CompaniesMax = _resolver.ResolveAgainstPlan(subscription, subscription.Plan, Guid.Empty).LimitOf(EntitlementKeys.CompaniesMax),
            Payments = paymentViews,
            PaymentDue = due == null ? null : new PaymentDueView(
                due.Plan.Code, due.Plan.Name, due.BillingCycle, due.Amount, due.Reason, due.AddOnsAmount,
                due.CreditAmount, due.UnusedDays),
            OnlinePaymentEnabled = _gateway.IsEnabled,
        };
    }

    /// <summary>
    /// The cheapest public plan that includes every feature the account has used and fits its
    /// known usage. Falls back to the plan flagged recommended. (The account's own overrides
    /// aren't considered -- a recommendation is about plans, not exceptions.)
    /// </summary>
    public static string? Recommend(AccountSubscription subscription, IReadOnlyList<Plan> plans, IReadOnlyList<CompanyUsageReport> usage)
    {
        var featuresUsed = usage.SelectMany(c => c.FeaturesUsed).ToHashSet();
        if (usage.Count > 1) featuresUsed.Add(EntitlementKeys.CompaniesMax);

        foreach (var plan in plans.OrderBy(p => p.SortOrder))
        {
            var view = ToView(plan);
            var hasFeatures = featuresUsed.All(key => key == EntitlementKeys.CompaniesMax
                ? Fits(view.Limits[key], usage.Count)
                : view.Features.GetValueOrDefault(key));
            var fitsLimits = usage.All(company => company.Limits
                .Where(kv => !EntitlementKeys.IsPooledLimit(kv.Key))
                .All(kv => kv.Value is not { } used || Fits(view.Limits.GetValueOrDefault(kv.Key), used)));
            // Employees and storage are one pool for the account: all companies together.
            fitsLimits &= EntitlementKeys.PooledLimits.All(key =>
                Fits(view.Limits.GetValueOrDefault(key), usage.Sum(c => c.Used.GetValueOrDefault(key) ?? 0)));
            if (hasFeatures && fitsLimits) return plan.Code;
        }

        return plans.FirstOrDefault(p => p.IsRecommended)?.Code ?? subscription.Plan?.Code;
    }

    private static bool Fits(int max, int used) => max == EntitlementKeys.Unlimited || used <= max;
}
