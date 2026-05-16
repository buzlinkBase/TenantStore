
using StackExchange.Redis;
using System.Text.Json;

namespace Onepunch.Common.Lib.Cache;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task RemoveAsync(string key);
    Task SetAsync<T>(string key, T value, TimeSpan expiry);
}

public class RedisCacheService : ICacheService
{
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
}