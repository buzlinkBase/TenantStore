using Microsoft.EntityFrameworkCore;
using Onepunch.Common.Lib.Subscriptions;
using Refit;
using Serilog;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>
/// A source of per-company usage counts. Composite: <see cref="SubscriptionUsageService"/> asks
/// every registered provider and merges what each knows -- Tenant Service counts admins itself,
/// hrms-api reports employees (and feature usage), hrms-adms-api reports biometric devices.
/// </summary>
public interface ICompanyUsageProvider
{
    /// <summary>Keys this provider reports: limit keys (how many are used) and feature keys (how
    /// much the feature has been used, e.g. payroll runs -- what the plan recommendation reads).</summary>
    IReadOnlyCollection<string> Keys { get; }

    /// <summary>Usage per company; a company missing from the result couldn't be counted.</summary>
    Task<IReadOnlyList<CompanyUsage>> GetUsageAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token);
}

/// <summary>hrms-api's and hrms-adms-api's internal usage endpoint (Api-key protected).</summary>
public interface ICompanyUsageApi
{
    [Get("/api/v1/internal/subscription-usage")]
    Task<ResponseModel<List<CompanyUsage>>> GetUsageAsync(
        [Query(CollectionFormat.Multi)] IEnumerable<Guid> tenantIds,
        CancellationToken token);
}

/// <summary>Usage reported by a remote service. Unreachable means unknown, never zero.</summary>
public sealed class RemoteCompanyUsageProvider(
    string name,
    ICompanyUsageApi api,
    IReadOnlyCollection<string> keys) : ICompanyUsageProvider
{
    public IReadOnlyCollection<string> Keys => keys;

    public async Task<IReadOnlyList<CompanyUsage>> GetUsageAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token)
    {
        if (tenantIds.Count == 0) return [];
        try
        {
            var response = await api.GetUsageAsync(tenantIds, token);
            return response.Data ?? [];
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Usage from {Service} unavailable for {Count} company(ies)", name, tenantIds.Count);
            return [];
        }
    }
}

/// <summary>Admins: members holding any role beyond the portal-only ones, excluding the hidden owner.
/// Pending invitations count too -- they hold a seat as soon as they're sent.</summary>
public sealed class AdminSeatUsageProvider(IUnitOfWorkService uow) : ICompanyUsageProvider
{
    private static readonly string[] PortalOnlyRoles = [TenantRoles.Employee, TenantRoles.Client];
    private static readonly string[] SeatStatuses = ["Active", "Invited"];

    public IReadOnlyCollection<string> Keys { get; } = [EntitlementKeys.AdminsMax];

    public async Task<IReadOnlyList<CompanyUsage>> GetUsageAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token)
    {
        var counts = await CountAsync(uow.Context, tenantIds, token);
        return tenantIds
            .Select(id => new CompanyUsage
            {
                TenantId = id,
                Used = new() { [EntitlementKeys.AdminsMax] = counts.GetValueOrDefault(id) },
            })
            .ToList();
    }

    public static async Task<Dictionary<Guid, int>> CountAsync(TenantContext context, IReadOnlyCollection<Guid> tenantIds, CancellationToken token) =>
        await context.Memberships
            .AsNoTracking()
            .Where(m => tenantIds.Contains(m.TenantId)
                && !m.IsHidden
                && SeatStatuses.Contains(m.Status)
                && m.Roles.Any(r => !PortalOnlyRoles.Contains(r.Role) && r.Role != TenantRoles.Owner))
            .GroupBy(m => m.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, token);
}

/// <summary>One company's usage, for the billing screen, downgrade checks and the plan recommendation.</summary>
public sealed record CompanyUsageReport(Guid TenantId, string CompanyName, Dictionary<string, int?> Used)
{
    /// <summary>Per-company limit keys; a null count is unknown.</summary>
    public IEnumerable<KeyValuePair<string, int?>> Limits => Used.Where(kv => EntitlementKeys.IsLimit(kv.Key));

    /// <summary>Features this company has actually used.</summary>
    public IEnumerable<string> FeaturesUsed => Used
        .Where(kv => EntitlementKeys.IsFeature(kv.Key) && kv.Value > 0)
        .Select(kv => kv.Key);
}

public class SubscriptionUsageService
{
    private readonly IUnitOfWorkService _uow;
    private readonly IEnumerable<ICompanyUsageProvider> _providers;

    public SubscriptionUsageService(IUnitOfWorkService uow, IEnumerable<ICompanyUsageProvider> providers)
    {
        _uow = uow;
        _providers = providers;
    }

    private TenantContext Context => _uow.Context;

    // Only activated companies (setup finished) are the account's: one still provisioning, or
    // whose setup failed or was abandoned, neither counts toward companies.max nor shows in billing.
    public Task<int> CountOwnedCompaniesAsync(Guid ownerUserId, CancellationToken token = default) =>
        Context.Tenants.AsNoTracking().CountAsync(t => t.UserId == ownerUserId && t.ActivatedAtUtc != null, token);

    public async Task<List<(Guid Id, string Name)>> OwnedCompaniesAsync(Guid ownerUserId, CancellationToken token = default)
    {
        var companies = await Context.Tenants.AsNoTracking()
            .Where(t => t.UserId == ownerUserId && t.ActivatedAtUtc != null)
            .OrderBy(t => t.TenantName)
            .Select(t => new { t.Id, t.TenantName })
            .ToListAsync(token);
        return companies.Select(c => (c.Id, c.TenantName ?? "")).ToList();
    }

    /// <summary>Usage for each company the account owns: every per-company limit (null when no
    /// provider could count it -- unknown, not zero) plus whatever feature usage providers report.</summary>
    public async Task<List<CompanyUsageReport>> GetCompanyUsageAsync(Guid ownerUserId, CancellationToken token = default)
    {
        var companies = await OwnedCompaniesAsync(ownerUserId, token);
        var ids = companies.Select(c => c.Id).ToList();

        var used = companies.ToDictionary(
            c => c.Id,
            _ => EntitlementKeys.Limits
                .Where(k => k != EntitlementKeys.CompaniesMax)
                .ToDictionary(k => k, _ => (int?)null));

        foreach (var provider in _providers)
        {
            foreach (var usage in await provider.GetUsageAsync(ids, token))
            {
                if (!used.TryGetValue(usage.TenantId, out var counts)) continue;
                foreach (var key in provider.Keys)
                {
                    if (usage.Used.TryGetValue(key, out var count)) counts[key] = count;
                }
            }
        }

        return companies.Select(c => new CompanyUsageReport(c.Id, c.Name, used[c.Id])).ToList();
    }
}
