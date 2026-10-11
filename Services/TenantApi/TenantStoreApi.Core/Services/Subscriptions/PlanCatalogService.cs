using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

public sealed record UpdatePlanCommand
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public int? SortOrder { get; init; }
    public bool? IsRecommended { get; init; }
    public bool? IsPublic { get; init; }
    /// <summary>New subscribers only -- existing ones keep their locked price.</summary>
    public decimal? MonthlyPrice { get; init; }
    public decimal? AnnualPrice { get; init; }
    public bool? PricesIncludeVat { get; init; }
    /// <summary>Internal note, console only. Unset: unchanged. Blank: cleared.</summary>
    public string? Remarks { get; init; }
}

public sealed record CreatePlanCommand
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? SortOrder { get; init; }
    public decimal MonthlyPrice { get; init; }
    public decimal AnnualPrice { get; init; }
    public bool PricesIncludeVat { get; init; } = true;
    /// <summary>Internal note for operators (what the plan is for), console only.</summary>
    public string? Remarks { get; init; }
    /// <summary>Start from another plan's entitlements. Unset: every feature off and every limit 0.</summary>
    public string? CopyEntitlementsFromCode { get; init; }
}

/// <summary>How many accounts a change to a plan reaches (entitlement edits apply to all of them at once).</summary>
public sealed record PlanImpact(string Code, int Accounts, Dictionary<AccountSubscriptionStatus, int> ByStatus);
/// <summary>
/// Plan maintenance for the platform. Price edits reach new subscribers only (existing ones keep
/// their locked price); entitlement edits reach everyone on the plan at once, so those accounts
/// are told to refresh.
/// </summary>
public class PlanCatalogService
{
    private readonly IUnitOfWorkService _uow;
    private readonly BillingEventService _events;
    private readonly SubscriptionChangeNotifierService _notifier;

    public PlanCatalogService(IUnitOfWorkService uow, BillingEventService events, SubscriptionChangeNotifierService notifier)
    {
        _uow = uow;
        _events = events;
        _notifier = notifier;
    }

    private TenantContext Context => _uow.Context;

    public Task<bool> CommitChangesAsync(CancellationToken token = default) => _uow.CommitChangesAsync("", token);

    public Task<List<Plan>> GetAllAsync(CancellationToken token = default) =>
        Context.Plans.AsNoTracking().Include(p => p.Entitlements).OrderBy(p => p.SortOrder).ToListAsync(token);

    public async Task<Plan> UpdateAsync(string code, UpdatePlanCommand command, string? actor, CancellationToken token = default)
    {
        var plan = await RequireAsync(code, token);
        if (command.MonthlyPrice is < 0 || command.AnnualPrice is < 0)
            throw new SubscriptionRuleException("Prices can't be negative.");

        if (command.Name is { } name && !string.IsNullOrWhiteSpace(name)) plan.Name = name.Trim();
        if (command.Description is { } description) plan.Description = description.Trim();
        if (command.SortOrder is { } order) plan.SortOrder = order;
        if (command.IsRecommended is { } recommended) plan.IsRecommended = recommended;
        if (command.IsPublic is { } isPublic) plan.IsPublic = isPublic;
        if (command.MonthlyPrice is { } monthly) plan.MonthlyPrice = monthly;
        if (command.AnnualPrice is { } annual) plan.AnnualPrice = annual;
        if (command.PricesIncludeVat is { } includesVat) plan.PricesIncludeVat = includesVat;
        if (command.Remarks != null) plan.Remarks = NormalizeRemarks(command.Remarks);

        await _events.AddAsync(null, "plan.updated", new { plan.Code, command }, actor, token: token);
        return plan;
    }

    public async Task SetEntitlementAsync(string code, string key, string value, string? actor, CancellationToken token = default)
    {
        key = key.Trim();
        value = value.Trim();
        if (!EntitlementKeys.IsKnown(key)) throw new SubscriptionRuleException($"Unknown entitlement '{key}'.");
        if (!EntitlementSnapshotBuilder.IsValidValue(key, value))
            throw new SubscriptionRuleException($"'{value}' isn't a valid value for {key}.");

        var plan = await RequireAsync(code, token);
        var entitlement = plan.Entitlements.FirstOrDefault(e => e.Key == key);
        if (entitlement == null)
        {
            await Context.PlanEntitlements.AddAsync(new PlanEntitlement
            {
                Id = Guid.CreateVersion7(),
                PlanId = plan.Id,
                Key = key,
                Value = value,
            }, token);
        }
        else
        {
            entitlement.Value = value;
        }

        await _events.AddAsync(null, "plan.entitlement-changed", new { plan.Code, key, value }, actor, token: token);

        var owners = await Context.AccountSubscriptions.AsNoTracking()
            .Where(s => s.PlanId == plan.Id)
            .Select(s => s.OwnerUserId)
            .ToListAsync(token);
        foreach (var owner in owners)
            await _notifier.PublishAsync(owner, "plan.entitlement-changed", token);
    }

    // ── Console operations ───────────────────────────────────────────────────────────────────

    public const int MaxRemarksLength = 1000;

    /// <summary>Trimmed; blank means none.</summary>
    private static string? NormalizeRemarks(string? remarks)
    {
        var trimmed = remarks?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > MaxRemarksLength)
            throw new SubscriptionRuleException($"Remarks can be at most {MaxRemarksLength} characters.");
        return trimmed;
    }

    private static readonly System.Text.RegularExpressions.Regex CodePattern = new("^[a-z][a-z0-9_-]{1,39}$");

    /// <summary>
    /// A new plan. It starts hidden (not public) so it can be set up before customers see it, and
    /// with every feature off and every limit 0 unless it copies another plan's entitlements.
    /// </summary>
    public async Task<Plan> CreateAsync(CreatePlanCommand command, string? actor, CancellationToken token = default)
    {
        var code = command.Code.Trim().ToLowerInvariant();
        if (!CodePattern.IsMatch(code))
            throw new SubscriptionRuleException("A plan code is 2-40 lowercase letters, digits, '-' or '_', starting with a letter.");
        if (string.IsNullOrWhiteSpace(command.Name)) throw new SubscriptionRuleException("A plan needs a name.");
        if (command.MonthlyPrice < 0 || command.AnnualPrice < 0) throw new SubscriptionRuleException("Prices can't be negative.");
        if (await Context.Plans.AnyAsync(p => p.Code == code, token) || Context.Plans.Local.Any(p => p.Code == code))
            throw new SubscriptionRuleException($"A plan with code '{code}' already exists.");

        Plan? source = null;
        if (!string.IsNullOrWhiteSpace(command.CopyEntitlementsFromCode))
            source = await RequireAsync(command.CopyEntitlementsFromCode.Trim(), token);

        var sortOrder = command.SortOrder
            ?? (await Context.Plans.AnyAsync(token) ? await Context.Plans.MaxAsync(p => p.SortOrder, token) + 10 : 10);
        var plan = new Plan
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = command.Name.Trim(),
            Description = command.Description?.Trim() ?? string.Empty,
            SortOrder = sortOrder,
            IsPublic = false,
            IsRecommended = false,
            MonthlyPrice = command.MonthlyPrice,
            AnnualPrice = command.AnnualPrice,
            Currency = "PHP",
            PricesIncludeVat = command.PricesIncludeVat,
            Remarks = NormalizeRemarks(command.Remarks),
        };
        await Context.Plans.AddAsync(plan, token);

        var values = source != null
            ? source.Entitlements.ToDictionary(e => e.Key, e => e.Value)
            : EntitlementKeys.Features.ToDictionary(k => k, _ => "false")
                .Concat(EntitlementKeys.Limits.ToDictionary(k => k, _ => "0"))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (key, value) in values)
        {
            await Context.PlanEntitlements.AddAsync(new PlanEntitlement { Id = Guid.CreateVersion7(), PlanId = plan.Id, Key = key, Value = value }, token);
        }

        await _events.AddAsync(null, "plan.created", new { plan.Code, plan.Name, copiedFrom = source?.Code }, actor, token: token);
        return plan;
    }

    /// <summary>
    /// Sets several entitlements of a plan at once, all or nothing: one invalid value changes
    /// nothing. Reaches every account on the plan immediately, so they're told once, not per key.
    /// Returns the keys whose value actually changed.
    /// </summary>
    public async Task<List<string>> SetEntitlementsAsync(
        string code, IReadOnlyDictionary<string, string> values, string? actor, CancellationToken token = default)
    {
        if (values.Count == 0) throw new SubscriptionRuleException("Nothing to change.");

        var cleaned = new Dictionary<string, string>();
        foreach (var (rawKey, rawValue) in values)
        {
            var key = rawKey.Trim();
            var value = rawValue.Trim();
            if (!EntitlementKeys.IsKnown(key)) throw new SubscriptionRuleException($"Unknown entitlement '{key}'.");
            if (!EntitlementSnapshotBuilder.IsValidValue(key, value))
                throw new SubscriptionRuleException($"'{value}' isn't a valid value for {key}.");
            cleaned[key] = value;
        }

        var plan = await RequireAsync(code, token);
        var changed = new Dictionary<string, (string? From, string To)>();
        foreach (var (key, value) in cleaned)
        {
            var entitlement = plan.Entitlements.FirstOrDefault(e => e.Key == key);
            if (entitlement == null)
            {
                await Context.PlanEntitlements.AddAsync(new PlanEntitlement { Id = Guid.CreateVersion7(), PlanId = plan.Id, Key = key, Value = value }, token);
                changed[key] = (null, value);
            }
            else if (entitlement.Value != value)
            {
                changed[key] = (entitlement.Value, value);
                entitlement.Value = value;
            }
        }
        if (changed.Count == 0) return [];

        await _events.AddAsync(null, "plan.entitlements-changed",
            new { plan.Code, changes = changed.ToDictionary(c => c.Key, c => new { from = c.Value.From, to = c.Value.To }) }, actor, token: token);

        var owners = await Context.AccountSubscriptions.AsNoTracking()
            .Where(s => s.PlanId == plan.Id)
            .Select(s => s.OwnerUserId)
            .ToListAsync(token);
        foreach (var owner in owners)
            await _notifier.PublishAsync(owner, "plan.entitlements-changed", token);
        return changed.Keys.ToList();
    }

    /// <summary>Accounts currently on each plan (by plan id) -- the "subscribers" column.</summary>
    public async Task<Dictionary<Guid, int>> GetSubscriberCountsAsync(CancellationToken token = default) =>
        (await Context.AccountSubscriptions.AsNoTracking()
            .GroupBy(s => s.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToListAsync(token))
        .ToDictionary(x => x.PlanId, x => x.Count);

    public async Task<PlanImpact> GetImpactAsync(string code, CancellationToken token = default)
    {
        var plan = await Context.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, token)
            ?? throw new KeyNotFoundException($"Unknown plan '{code}'.");
        var byStatus = (await Context.AccountSubscriptions.AsNoTracking()
                .Where(s => s.PlanId == plan.Id)
                .GroupBy(s => s.SubStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(token))
            .ToDictionary(x => x.Status, x => x.Count);
        return new PlanImpact(plan.Code, byStatus.Values.Sum(), byStatus);
    }

    private async Task<Plan> RequireAsync(string code, CancellationToken token) =>
        await Context.Plans.Include(p => p.Entitlements).FirstOrDefaultAsync(p => p.Code == code, token)
        ?? throw new KeyNotFoundException($"Unknown plan '{code}'.");
}
