using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace Onepunch.Common.Lib.Security;

/// <summary>Shared storage for the Auth service's JWKS document and a short "fetch failed" marker.</summary>
public interface IJwksStore
{
    string? GetJwks();
    void SetJwks(string json, TimeSpan ttl);
    bool IsFetchFailing();
    void MarkFetchFailed(TimeSpan ttl);
}

/// <summary>
/// Redis-backed <see cref="IJwksStore"/>: every API instance shares one copy of the JWKS, so the Auth
/// service is hit once per TTL across the whole deployment instead of once per process. Redis errors
/// are swallowed (treated as a miss) so an unavailable Redis only costs a direct fetch.
/// </summary>
public sealed class RedisJwksStore(IConnectionMultiplexer redis) : IJwksStore
{
    public const string JwksKey = "jwks:auth";
    public const string FailKey = "jwks:auth:fail";

    public string? GetJwks() => Try(() => (string?)redis.GetDatabase().StringGet(JwksKey));

    public void SetJwks(string json, TimeSpan ttl) =>
        Try(() => redis.GetDatabase().StringSet(JwksKey, json, ttl));

    public bool IsFetchFailing() => Try(() => redis.GetDatabase().KeyExists(FailKey));

    public void MarkFetchFailed(TimeSpan ttl) =>
        Try(() => redis.GetDatabase().StringSet(FailKey, "1", ttl));

    private static T? Try<T>(Func<T> action)
    {
        try { return action(); }
        catch (RedisException) { return default; }
        catch (TimeoutException) { return default; }
    }
}

/// <summary>
/// Signing keys for JWT validation, fetched from the Auth service's JWKS endpoint.
///
/// Replaces an inline resolver that did a blocking HTTP call per request whenever its cache was
/// stale and never advanced the expiry on failure -- so while Auth was slow or down, every request
/// blocked on its own fetch (100s default timeout), starving the thread pool and piling requests
/// (and memory) up. Now: in-process keys first, then the shared Redis copy, then a single-flight
/// fetch with a short timeout; a failed fetch is remembered in Redis for <see cref="FailBackoff"/>
/// so no instance retries until it expires, and the last known keys keep being served meanwhile.
/// </summary>
public sealed class JwksKeyCache
{
    public static readonly TimeSpan LocalTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SharedTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan FailBackoff = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IJwksStore _store;
    private readonly string _jwksUrl;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private readonly object _sync = new();

    private IReadOnlyList<SecurityKey> _keys = [];
    private string? _keysSource;
    private DateTimeOffset _localExpiry = DateTimeOffset.MinValue;

    public const string DefaultJwksPath = "/.well-known/jwks.json";

    /// <param name="jwksPath">Where the keys are published; defaults to the tenant users' JWKS. The platform
    /// operators' keys live at a different path (see PlatformAuthDefaults.JwksPath).</param>
    public JwksKeyCache(IHttpClientFactory httpClientFactory, IJwksStore store, string authUrl, TimeProvider? time = null, string jwksPath = DefaultJwksPath)
    {
        _httpClientFactory = httpClientFactory;
        _store = store;
        _jwksUrl = authUrl.TrimEnd('/') + "/" + jwksPath.TrimStart('/');
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyList<SecurityKey> GetKeys()
    {
        lock (_sync)
        {
            if (_time.GetUtcNow() < _localExpiry) return _keys;
        }

        var shared = _store.GetJwks();
        if (!string.IsNullOrEmpty(shared)) return Accept(shared);

        if (_store.IsFetchFailing()) return Current();

        // Single flight: whoever doesn't get the gate serves the last known keys instead of
        // queueing another blocking HTTP call behind the one already in progress.
        if (!_fetchGate.Wait(0)) return Current();
        try
        {
            var json = Fetch();
            if (json == null)
            {
                _store.MarkFetchFailed(FailBackoff);
                // Back off locally too, so this instance doesn't retry on every request even
                // when the shared store is unavailable.
                lock (_sync) _localExpiry = _time.GetUtcNow().Add(FailBackoff);
                return Current();
            }
            _store.SetJwks(json, SharedTtl);
            return Accept(json);
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private string? Fetch()
    {
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(JwksKeyCache));
            using var cts = new CancellationTokenSource(FetchTimeout);
            var json = client.GetStringAsync(_jwksUrl, cts.Token).GetAwaiter().GetResult();
            // Parse once here so a malformed document counts as a failed fetch.
            _ = new JsonWebKeySet(json);
            return json;
        }
        catch (Exception)
        {
            // Network error, timeout, non-2xx or malformed document -- all mean "no usable keys".
            return null;
        }
    }

    private IReadOnlyList<SecurityKey> Accept(string json)
    {
        lock (_sync)
        {
            // Re-parse only when the document actually changed.
            if (json != _keysSource)
            {
                try
                {
                    _keys = new JsonWebKeySet(json).GetSigningKeys().ToList();
                    _keysSource = json;
                }
                catch (Exception)
                {
                    // A corrupt shared copy: keep serving the last good keys.
                    return _keys;
                }
            }
            _localExpiry = _time.GetUtcNow().Add(LocalTtl);
            return _keys;
        }
    }

    private IReadOnlyList<SecurityKey> Current()
    {
        lock (_sync) return _keys;
    }
}

/// <summary>Used when no Redis is configured: nothing is shared, so every lookup falls through to the in-process cache and fetch.</summary>
public sealed class NullJwksStore : IJwksStore
{
    public string? GetJwks() => null;
    public void SetJwks(string json, TimeSpan ttl) { }
    public bool IsFetchFailing() => false;
    public void MarkFetchFailed(TimeSpan ttl) { }
}
