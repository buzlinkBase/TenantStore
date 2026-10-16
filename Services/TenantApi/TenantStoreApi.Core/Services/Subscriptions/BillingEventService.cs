using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using TenantStoreApi.Domain.Entities.Subs;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>Writes and reads the billing history (<see cref="BillingEvent"/>). Doesn't commit.</summary>
public class BillingEventService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWorkService _uow;
    private readonly TimeProvider _clock;

    public BillingEventService(IUnitOfWorkService uow, TimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;

    public async Task AddAsync(
        AccountSubscription? subscription,
        string type,
        object? payload = null,
        string? actor = null,
        string? dedupeKey = null,
        Guid? ownerUserId = null,
        CancellationToken token = default)
    {
        await Context.BillingEvents.AddAsync(new BillingEvent
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription?.Id,
            OwnerUserId = subscription?.OwnerUserId ?? ownerUserId,
            Type = type,
            PayloadJson = payload == null ? null : JsonSerializer.Serialize(payload, Json),
            OccurredAtUtc = _clock.GetUtcNow().UtcDateTime,
            DedupeKey = dedupeKey,
            Actor = actor,
        }, token);
    }

    /// <summary>Whether work identified by <paramref name="dedupeKey"/> was already done -- also
    /// counting an event added in this unit of work but not yet saved.</summary>
    public async Task<bool> ExistsAsync(string dedupeKey, CancellationToken token = default) =>
        Context.BillingEvents.Local.Any(e => e.DedupeKey == dedupeKey)
        || await Context.BillingEvents.AsNoTracking().AnyAsync(e => e.DedupeKey == dedupeKey, token);

    public Task<List<BillingEvent>> ListAsync(Guid ownerUserId, int take = 100, CancellationToken token = default) =>
        Context.BillingEvents.AsNoTracking()
            .Where(e => e.OwnerUserId == ownerUserId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(token);
}

/// <summary>
/// Tells every other service an account's entitlements may have changed (<see cref="SubscriptionChanged"/>):
/// hrms-api evicts its cache for each company, Auth Service refreshes members' live sessions.
/// Published through the outbox, so it leaves only when the change itself commits.
/// </summary>
public class SubscriptionChangeNotifierService
{
    private readonly IUnitOfWorkService _uow;
    private readonly IPublishEndpoint _publisher;
    private readonly IClientStandingPublisher _clientStanding;

    public SubscriptionChangeNotifierService(IUnitOfWorkService uow, IPublishEndpoint publisher, IClientStandingPublisher clientStanding)
    {
        _uow = uow;
        _publisher = publisher;
        _clientStanding = clientStanding;
    }

    public async Task PublishAsync(Guid ownerUserId, string reason, CancellationToken token = default)
    {
        var context = _uow.Context;
        var tenantIds = await context.Tenants.AsNoTracking()
            .Where(t => t.UserId == ownerUserId)
            .Select(t => t.Id)
            .ToListAsync(token);

        var memberUserIds = await context.Memberships.AsNoTracking()
            .InternalOnly()
            .Where(m => tenantIds.Contains(m.TenantId) && m.UserId != Guid.Empty && m.Status == "Active")
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(token);

        await _publisher.Publish(new SubscriptionChanged
        {
            OwnerUserId = ownerUserId,
            TenantIds = tenantIds,
            MemberUserIds = memberUserIds,
            Reason = reason,
        }, token);

        // The agency's clients depend on its standing too (suspended or held = blocked), so the portal hears of it.
        await _clientStanding.PublishAsync(tenantIds, reason, token);
    }
}
