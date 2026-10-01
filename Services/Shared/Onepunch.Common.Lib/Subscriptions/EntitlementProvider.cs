using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Onepunch.Common.Lib.Cache;
using Refit;
using Serilog;

namespace Onepunch.Common.Lib.Subscriptions;

/// <summary>Tenant Service's internal entitlements endpoint (X-Api-Key protected).</summary>
public interface ITenantEntitlementApi
{
    [Get("/api/v1/internal/subscriptions/entitlements/{tenantId}")]
    Task<ResponseModel<EntitlementSnapshot>> GetAsync(Guid tenantId, CancellationToken token);
}

/// <summary>
/// A company's entitlements, for services enforcing them (hrms-api, hrms-adms-api). Tenant
/// Service resolves; this caches.
/// </summary>
public interface IEntitlementProvider
{
    Task<EntitlementSnapshot> GetAsync(Guid tenantId, CancellationToken token = default);

    /// <summary>Drops cached snapshots -- on SubscriptionChanged.</summary>
    Task InvalidateAsync(IEnumerable<Guid> tenantIds);
}

/// <summary>
/// Redis-cached for five minutes (and memoized per request scope), evicted on SubscriptionChanged.
/// If Tenant Service can't be reached it fails open -- briefly, with a short cache -- since
/// blocking every company's writes because one dependency is down would be worse than letting a
/// few minutes of over-limit activity through.
/// </summary>
public sealed class CachedEntitlementProvider(ITenantEntitlementApi api, ICacheService cache) : IEntitlementProvider
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailOpenFor = TimeSpan.FromSeconds(30);

    private readonly Dictionary<Guid, EntitlementSnapshot> _scoped = new();

    public static string CacheKey(Guid tenantId) => $"entitlements:{tenantId}";

    public async Task<EntitlementSnapshot> GetAsync(Guid tenantId, CancellationToken token = default)
    {
        if (tenantId == Guid.Empty) return EntitlementSnapshot.Unrestricted(tenantId);
        if (_scoped.TryGetValue(tenantId, out var memo)) return memo;

        var key = CacheKey(tenantId);
        var snapshot = await TryCacheGetAsync(key);
        if (snapshot == null)
        {
            try
            {
                var response = await api.GetAsync(tenantId, token);
                snapshot = response.Data ?? throw new InvalidOperationException("Empty entitlements response.");
                await TryCacheSetAsync(key, snapshot, CacheFor);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Logger.Warning(ex, "Entitlements for tenant {TenantId} unavailable; allowing everything for {Seconds}s", tenantId, FailOpenFor.TotalSeconds);
                snapshot = EntitlementSnapshot.Unrestricted(tenantId) with { Status = "Unavailable" };
                await TryCacheSetAsync(key, snapshot, FailOpenFor);
            }
        }

        _scoped[tenantId] = snapshot;
        return snapshot;
    }

    public async Task InvalidateAsync(IEnumerable<Guid> tenantIds)
    {
        foreach (var tenantId in tenantIds)
        {
            _scoped.Remove(tenantId);
            try { await cache.RemoveAsync(CacheKey(tenantId)); }
            catch (Exception ex) { Log.Logger.Warning(ex, "Couldn't evict cached entitlements for tenant {TenantId}", tenantId); }
        }
    }

    // Redis being down mustn't take enforcement down with it -- go straight to Tenant Service.
    private async Task<EntitlementSnapshot?> TryCacheGetAsync(string key)
    {
        try { return await cache.GetAsync<EntitlementSnapshot>(key); }
        catch (Exception ex) { Log.Logger.Warning(ex, "Entitlement cache read failed for {Key}", key); return null; }
    }

    private async Task TryCacheSetAsync(string key, EntitlementSnapshot snapshot, TimeSpan expiry)
    {
        try { await cache.SetAsync(key, snapshot, expiry); }
        catch (Exception ex) { Log.Logger.Warning(ex, "Entitlement cache write failed for {Key}", key); }
    }
}

public static class EntitlementServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IEntitlementProvider"/> against Tenant Service at
    /// <paramref name="tenantServiceUrl"/>, authenticating with its service-to-service
    /// <paramref name="apiKey"/> (Tenant Service's ApiKeySettings:ApiKey). Needs an
    /// <see cref="ICacheService"/> registered.
    /// </summary>
    public static IServiceCollection AddTenantEntitlements(this IServiceCollection services, string tenantServiceUrl, string? apiKey)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());

        services.AddRefitClient<ITenantEntitlementApi>(new RefitSettings { ContentSerializer = new SystemTextJsonContentSerializer(json) })
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(tenantServiceUrl);
                client.Timeout = TimeSpan.FromSeconds(5);
                if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            });
        services.AddScoped<IEntitlementProvider, CachedEntitlementProvider>();
        return services;
    }
}
