using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>Where an agency (a company that runs the Client Portal) stands with the platform, as far as its clients are concerned.</summary>
public enum AgencyStanding
{
    Good = 0,

    /// <summary>The account is suspended (by an operator, or by the lifecycle after the grace period).</summary>
    Suspended = 1,

    /// <summary>The account is under legal hold.</summary>
    LegalHold = 2,
}

public static class AgencyStandingExtensions
{
    /// <summary>A suspended or held agency's clients can't use the portal.</summary>
    public static bool BlocksClients(this AgencyStanding standing) => standing != AgencyStanding.Good;
}

/// <summary>Strategy: how an agency's standing is worked out, so tests and other sources can stand in.</summary>
public interface IAgencyStandingProvider
{
    /// <summary>Standing per company (agency). Every asked company is present; one without a subscription is Good.</summary>
    Task<IReadOnlyDictionary<Guid, AgencyStanding>> GetAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token = default);
}

/// <summary>Reads the standing from the owner account's subscription: Suspended status, or legal hold.</summary>
public sealed class AgencyStandingProvider(IUnitOfWorkService uow) : IAgencyStandingProvider
{
    public async Task<IReadOnlyDictionary<Guid, AgencyStanding>> GetAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token = default)
    {
        var result = tenantIds.Distinct().ToDictionary(id => id, _ => AgencyStanding.Good);
        if (result.Count == 0) return result;

        var context = uow.Context;
        var ids = result.Keys.ToList();
        var owners = await context.Tenants.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.UserId })
            .ToListAsync(token);
        var ownerIds = owners.Select(o => o.UserId).Distinct().ToList();
        var subscriptions = await context.AccountSubscriptions.AsNoTracking()
            .Where(s => ownerIds.Contains(s.OwnerUserId))
            .Select(s => new { s.OwnerUserId, s.SubStatus, s.LegalHold })
            .ToDictionaryAsync(s => s.OwnerUserId, token);

        foreach (var owner in owners)
        {
            if (!subscriptions.TryGetValue(owner.UserId, out var subscription)) continue;
            result[owner.Id] = subscription.SubStatus == AccountSubscriptionStatus.Suspended ? AgencyStanding.Suspended
                : subscription.LegalHold ? AgencyStanding.LegalHold
                : AgencyStanding.Good;
        }
        return result;
    }
}
