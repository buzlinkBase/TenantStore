
using StackExchange.Redis;
using System.Text.Json;

namespace Onepunch.Common.Lib.Cache;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task RemoveAsync(string key);
    Task SetAsync<T>(string key, T value, TimeSpan expiry);
    // Distributed lock (SETNX-style) for coordinating work across multiple app instances behind
    // a load balancer -- e.g. a scheduled job that must run exactly once per key even though
    // every instance's own timer fires independently. Returns a lock token on success, or null
    // if another holder already has it.
    Task<string?> TryAcquireLockAsync(string key, TimeSpan expiry);
    // Releases the lock only if `token` still matches the current holder (compare-and-delete),
    // so an instance can never release a lock some OTHER instance has since acquired after this
    // one's own lock already expired mid-run.
    Task<bool> ReleaseLockAsync(string key, string token);
}

public class RedisCacheService : ICacheService
{
    // Standard Redis compare-and-delete pattern for safely releasing a SETNX-style lock: only
    // delete the key if it still holds the value this caller set, never someone else's.
    private const string ReleaseLockScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private readonly IDatabase _db;
    public RedisCacheService(IConnectionMultiplexer redis)
    {

        _db = redis.GetDatabase();
    }
    public async Task<T?> GetAsync<T>(string key)
    {
        var cache = await _db.StringGetAsync(key);
        if (cache.IsNullOrEmpty)
            return default;

        return JsonSerializer.Deserialize<T>(cache!);
    }
    public async Task SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        var json = JsonSerializer.Serialize(value);
        await _db.StringSetAsync(key, json, expiry);
    }
    public async Task RemoveAsync(string key)
    {
        await _db.KeyDeleteAsync(key);
    }
    public async Task<string?> TryAcquireLockAsync(string key, TimeSpan expiry)
    {
        var token = Guid.NewGuid().ToString("N");
        var acquired = await _db.StringSetAsync(key, token, expiry, When.NotExists);
        return acquired ? token : null;
    }
    public async Task<bool> ReleaseLockAsync(string key, string token)
    {
        var result = await _db.ScriptEvaluateAsync(ReleaseLockScript, new RedisKey[] { key }, new RedisValue[] { token });
        return (long)result == 1;
    }
}