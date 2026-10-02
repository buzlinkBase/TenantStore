using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Onepunch.Auth.Core;
using Serilog;
using TenantStoreApi.Infrastructure;

namespace TenantStoreApi.Core.Services.Subscriptions;

/// <summary>Which companies finished setting up, and when. Strategy, so tests can stand in for AuthApi.</summary>
public interface IProvisionedTenantSource
{
    /// <summary>Ready tenant → when it became ready (UTC). Empty when the source can't answer.</summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetReadyAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token);
}

/// <summary>AuthApi's record of setup finishing (TenantCreationRequests.HrDbReady), over gRPC.</summary>
public sealed class AuthProvisionedTenantSource(TenantProvisioningService.TenantProvisioningServiceClient client)
    : IProvisionedTenantSource
{
    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetReadyAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token)
    {
        if (tenantIds.Count == 0) return new Dictionary<Guid, DateTime>();
        try
        {
            var request = new ReadyTenantsRequest();
            request.TenantIds.AddRange(tenantIds.Select(id => id.ToString()));
            var response = await client.GetReadyTenantsAsync(request, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: token);
            return response.Tenants
                .Where(t => Guid.TryParse(t.TenantId, out _))
                .ToDictionary(
                    t => Guid.Parse(t.TenantId),
                    t => DateTime.TryParse(t.ReadyAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
                        ? at
                        : DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Logger.Warning(ex, "Couldn't ask AuthApi which of {Count} company(ies) finished setting up; retrying later", tenantIds.Count);
            return new Dictionary<Guid, DateTime>();
        }
    }
}

/// <summary>
/// Marks companies activated that finished setting up but were never marked -- every company
/// that existed before activation tracking, and any whose provisioning message was missed.
/// A company that finished recently (within a trial's length) is a new signup whose message went
/// missing: its owner starts their one trial, as TenantActivatedWorker would have done. One that
/// finished earlier is an existing customer: SubscriptionBackfillService, which runs right after,
/// gives the owner the legacy plan instead. Doesn't commit: the caller owns the unit of work.
/// </summary>
public class CompanyActivationSyncService
{
    private const int BatchSize = 500;

    private readonly IUnitOfWorkService _uow;
    private readonly IProvisionedTenantSource _source;
    private readonly AccountSubscriptionService _subscriptions;
    private readonly SubscriptionOptions _options;
    private readonly TimeProvider _clock;

    public CompanyActivationSyncService(
        IUnitOfWorkService uow,
        IProvisionedTenantSource source,
        AccountSubscriptionService subscriptions,
        Microsoft.Extensions.Options.IOptions<SubscriptionOptions> options,
        TimeProvider clock)
    {
        _uow = uow;
        _source = source;
        _subscriptions = subscriptions;
        _options = options.Value;
        _clock = clock;
    }

    private TenantContext Context => _uow.Context;

    /// <returns>How many companies were activated.</returns>
    public async Task<int> SyncAsync(CancellationToken token)
    {
        var pending = await Context.Tenants.AsNoTracking()
            .Where(t => t.ActivatedAtUtc == null)
            .Select(t => t.Id)
            .ToListAsync(token);

        var recentSince = _clock.GetUtcNow().UtcDateTime.AddDays(-_options.TrialDays);
        var activated = 0;
        foreach (var batch in pending.Chunk(BatchSize))
        {
            var ready = await _source.GetReadyAsync(batch, token);
            if (ready.Count == 0) continue;

            var readyIds = ready.Keys.ToList();
            var tenants = await Context.Tenants
                .Where(t => readyIds.Contains(t.Id) && t.ActivatedAtUtc == null)
                .ToListAsync(token);
            foreach (var tenant in tenants)
            {
                var readyAt = ready[tenant.Id];
                tenant.ActivatedAtUtc = readyAt;
                activated++;
                if (readyAt >= recentSince && tenant.UserId != Guid.Empty)
                {
                    var ownerEmail = await Context.Memberships.AsNoTracking()
                        .Where(m => m.TenantId == tenant.Id && m.UserId == tenant.UserId && m.IsHidden)
                        .Select(m => m.InvitedEmail)
                        .FirstOrDefaultAsync(token);
                    await _subscriptions.StartTrialIfFirstCompanyAsync(tenant.UserId, ownerEmail, token);
                }
            }
        }

        if (activated > 0)
        {
            await _uow.SaveChangesAsync(token);
            Log.Logger.Information("Company activation sync: {Count} company(ies) marked activated", activated);
        }
        return activated;
    }
}