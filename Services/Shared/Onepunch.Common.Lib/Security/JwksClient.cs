using Microsoft.IdentityModel.Tokens;

namespace Onepunch.Common.Lib.Security;

/// <summary>
/// Fetches and caches the Auth Service's JWKS document so other services can validate
/// RS256-signed JWTs without sharing a secret. Registered via AddHttpClient&lt;JwksClient&gt;
/// with its BaseAddress pointed at the Auth Service's /.well-known/jwks.json.
/// </summary>
public class JwksClient
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient;
    private readonly object _lock = new();
    private List<JsonWebKey> _keys = new();
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    public JwksClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public IEnumerable<SecurityKey> ResolveSigningKey(
        string token,
        SecurityToken securityToken,
        string kid,
        TokenValidationParameters validationParameters)
    {
        var keys = GetKeys(kid);
        return string.IsNullOrEmpty(kid)
            ? keys
            : keys.Where(k => string.Equals(k.Kid, kid, StringComparison.Ordinal));
    }

    private List<JsonWebKey> GetKeys(string? kid)
    {
        bool haveRequestedKid;
        bool stale;
        lock (_lock)
        {
            haveRequestedKid = string.IsNullOrEmpty(kid) || _keys.Any(k => k.Kid == kid);
            stale = DateTime.UtcNow - _lastRefreshUtc > RefreshInterval;
        }

        if (stale || !haveRequestedKid)
        {
            Refresh();
        }

        lock (_lock)
        {
            return _keys.ToList();
        }
    }

    private void Refresh()
    {
        try
        {
            // Synchronous-over-async is safe here: ASP.NET Core has no SynchronizationContext,
            // and IssuerSigningKeyResolver is a synchronous delegate with no async overload.
            var json = _httpClient.GetStringAsync(".well-known/jwks.json").GetAwaiter().GetResult();
            var jwks = new JsonWebKeySet(json);
            lock (_lock)
            {
                _keys = jwks.Keys.ToList();
            }
            _lastRefreshUtc = DateTime.UtcNow;
        }
        catch
        {
            // Keep serving the last known-good key set on a transient fetch failure.
        }
    }
}
