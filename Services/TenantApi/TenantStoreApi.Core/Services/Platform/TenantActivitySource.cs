using System.Globalization;
using Onepunch.Auth.Core;
using Serilog;

namespace TenantStoreApi.Core.Services.Platform;

/// <summary>When each company last had an HRIS sign-in. Strategy, so tests can stand in for AuthApi.</summary>
public interface ITenantActivitySource
{
    /// <summary>Last sign-in per company. Companies nobody has signed in to (or when AuthApi can't answer) are left out.</summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetLastActiveAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token);
}

/// <summary>Used when no activity source is wired: the console simply shows no "last active".</summary>
public sealed class NoTenantActivitySource : ITenantActivitySource
{
    public static NoTenantActivitySource Instance { get; } = new();

    public Task<IReadOnlyDictionary<Guid, DateTime>> GetLastActiveAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token) =>
        Task.FromResult<IReadOnlyDictionary<Guid, DateTime>>(new Dictionary<Guid, DateTime>());
}

/// <summary>AuthApi's record of company sign-ins, over gRPC. Never throws: a failure just leaves the column empty.</summary>
public sealed class AuthTenantActivitySource(TenantActivityService.TenantActivityServiceClient client) : ITenantActivitySource
{
    private const int IdsPerCall = 1000;

    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetLastActiveAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken token)
    {
        var result = new Dictionary<Guid, DateTime>();
        try
        {
            foreach (var chunk in tenantIds.Distinct().Chunk(IdsPerCall))
            {
                var request = new TenantActivityRequest();
                request.TenantIds.AddRange(chunk.Select(id => id.ToString()));
                var response = await client.GetLastActiveAsync(request, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: token);
                foreach (var row in response.Tenants)
                {
                    if (Guid.TryParse(row.TenantId, out var id)
                        && DateTime.TryParse(row.LastActiveAtUtc, CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
                        result[id] = at;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Logger.Warning(ex, "Couldn't read company activity from AuthApi; the console will leave last active out");
            return new Dictionary<Guid, DateTime>();
        }
        return result;
    }
}
