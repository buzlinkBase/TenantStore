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
}

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

    private async Task<Plan> RequireAsync(string code, CancellationToken token) =>
        await Context.Plans.Include(p => p.Entitlements).FirstOrDefaultAsync(p => p.Code == code, token)
        ?? throw new KeyNotFoundException($"Unknown plan '{code}'.");
}
