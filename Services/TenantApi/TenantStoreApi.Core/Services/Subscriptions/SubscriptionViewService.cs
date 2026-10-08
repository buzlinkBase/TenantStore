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
    decimal AmountDue);

/// <summary>What Pay now would pay for (see AccountSubscriptionService.GetPaymentDueAsync).</summary>
public sealed record PaymentDueView(
    string PlanCode,
    string PlanName,
    BillingCycle BillingCycle,
    decimal Amount,
    PaymentDueReason Reason);

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
    DateTime PeriodEndUtc);

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
    public List<CompanyUsageView> Companies { get; init; } = [];
    public List<PaymentView> Payments { get; init; } = [];
    /// <summary>The cheapest plan covering everything the account has used and fits its usage --
    /// what the plan selection screen pre-selects.</summary>
    public string? RecommendedPlanCode { get; init; }
    public string PaymentInstructions { get; init; } = string.Empty;
    /// <summary>What the account can pay for now; null when nothing is due.</summary>
    public PaymentDueView? PaymentDue { get; init; }
    /// <summary>Whether Pay now (the payment gateway's hosted page) is offered.</summary>
    public bool OnlinePaymentEnabled { get; init; }
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

    public SubscriptionViewService(
        AccountSubscriptionService subscriptions,
        EntitlementResolverService resolver,
        SubscriptionUsageService usage,
        IOptions<SubscriptionOptions> options,
        TimeProvider clock,
        IPaymentGateway gateway)
    {
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

    public async Task<List<PlanView>> GetPublicPlansAsync(CancellationToken token = default) =>
        (await _subscriptions.GetPublicPlansAsync(token)).Select(ToView).ToList();

    public async Task<EntitlementsView> GetEntitlementsAsync(Guid tenantId, CancellationToken token = default)
    {
        var snapshot = await _resolver.ResolveForTenantAsync(tenantId, token);
        var plans = await _subscriptions.GetPublicPlansAsync(token);
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
        var plans = await _subscriptions.GetPublicPlansAsync(token);
        var usage = await _usage.GetCompanyUsageAsync(owner, token);

        PendingChangeView? pending = null;
        if (subscription.PendingPlanId is { } pendingPlanId
            && await _subscriptions.FindPlanAsync(pendingPlanId, token) is { } pendingPlan)
        {
            var cycle = subscription.PendingBillingCycle ?? subscription.BillingCycle;
            pending = new PendingChangeView(
                pendingPlan.Code, pendingPlan.Name, cycle,
                subscription.PendingChangeTiming ?? PlanChangeTiming.OnPayment,
                subscription.PendingChangeEffectiveAtUtc,
                await _subscriptions.PriceForAsync(subscription, pendingPlan, cycle, token));
        }

        var payments = await _subscriptions.GetPaymentsAsync(subscription.Id, token: token);
        var planNames = plans.ToDictionary(p => p.Id, p => p.Name);
        planNames.TryAdd(subscription.PlanId, subscription.Plan.Name);
        var due = await _subscriptions.GetPaymentDueAsync(subscription, token);

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
            CompaniesUsed = usage.Count,
            CompaniesMax = _resolver.ResolveAgainstPlan(subscription, subscription.Plan, Guid.Empty).LimitOf(EntitlementKeys.CompaniesMax),
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
            Payments = payments.Select(p => new PaymentView(
                p.Id, p.PaidAtUtc, p.Amount, p.Currency, p.Method, p.Reference,
                planNames.GetValueOrDefault(p.PlanId), p.BillingCycle, p.PeriodStartUtc, p.PeriodEndUtc)).ToList(),
            RecommendedPlanCode = Recommend(subscription, plans, usage),
            PaymentDue = due == null ? null : new PaymentDueView(due.Plan.Code, due.Plan.Name, due.BillingCycle, due.Amount, due.Reason),
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
            var fitsLimits = usage.All(company => company.Limits.All(kv =>
                kv.Value is not { } used || Fits(view.Limits.GetValueOrDefault(kv.Key), used)));
            if (hasFeatures && fitsLimits) return plan.Code;
        }

        return plans.FirstOrDefault(p => p.IsRecommended)?.Code ?? subscription.Plan?.Code;
    }

    private static bool Fits(int max, int used) => max == EntitlementKeys.Unlimited || used <= max;
}
