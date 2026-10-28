using Microsoft.Extensions.Options;
using Onepunch.Common.Lib.DTO;
using TenantStoreApi.Core.Services.Subscriptions;
using TenantStoreApi.Domain.Entities.Subs;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>
/// Which services have confirmed a company's data is gone, for an operator's erasure. Each
/// confirmation is a keyed billing event (<see cref="BillingEventTypes.HardDeleteCompanyDone"/>) per
/// company and service, so a redelivered answer adds nothing; an erasure finishes only when every
/// participant (<see cref="SubscriptionOptions.ErasureParticipants"/>) has confirmed every company.
/// </summary>
public class ErasureConfirmations
{
    private readonly BillingEventService _events;

    public ErasureConfirmations(BillingEventService events, IOptions<SubscriptionOptions> options)
    {
        _events = events;
        Participants = Parse(options.Value.ErasureParticipants);
    }

    /// <summary>The services that must confirm, hrms-api always among them.</summary>
    public IReadOnlyList<string> Participants { get; }

    public bool IsParticipant(string source) => Participants.Contains(source);

    /// <summary>Records one service's confirmation for one company (once).</summary>
    public async Task ConfirmAsync(AccountSubscription subscription, CompanyDataPurged result, string source, CancellationToken token)
    {
        var key = Key(subscription.Id, result.TenantId, source);
        if (await _events.ExistsAsync(key, token)) return;
        await _events.AddAsync(subscription, BillingEventTypes.HardDeleteCompanyDone,
            new { result.TenantId, source, result.RowsDeleted, result.FilesDeleted }, actor: "system", dedupeKey: key, token: token);
    }

    /// <summary>The services that haven't confirmed the company yet.</summary>
    public async Task<IReadOnlyList<string>> PendingAsync(Guid subscriptionId, Guid tenantId, CancellationToken token)
    {
        var pending = new List<string>();
        foreach (var source in Participants)
        {
            if (!await _events.ExistsAsync(Key(subscriptionId, tenantId, source), token)) pending.Add(source);
        }
        return pending;
    }

    /// <summary>The companies some service hasn't confirmed yet.</summary>
    public async Task<List<Guid>> PendingCompaniesAsync(Guid subscriptionId, IEnumerable<Guid> tenantIds, CancellationToken token)
    {
        var pending = new List<Guid>();
        foreach (var tenantId in tenantIds)
        {
            if ((await PendingAsync(subscriptionId, tenantId, token)).Count > 0) pending.Add(tenantId);
        }
        return pending;
    }

    // hrms-api's key keeps the format it had before the other services answered, so an erasure under
    // way across a deploy still counts its confirmations.
    private static string Key(Guid subscriptionId, Guid tenantId, string source) =>
        source == PurgeSources.Hrms
            ? $"{BillingEventTypes.HardDeleteCompanyDone}:{subscriptionId:N}:{tenantId:N}"
            : $"{BillingEventTypes.HardDeleteCompanyDone}:{subscriptionId:N}:{tenantId:N}:{source}";

    private static List<string> Parse(string? configured)
    {
        var sources = (configured ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(PurgeSources.From)
            .ToList();
        if (!sources.Contains(PurgeSources.Hrms)) sources.Insert(0, PurgeSources.Hrms);
        return sources.Distinct().ToList();
    }
}
