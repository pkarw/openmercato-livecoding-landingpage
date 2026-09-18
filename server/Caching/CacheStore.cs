using System.Text.Json;
using StackExchange.Redis;

namespace Landing.Caching;

/// <summary>
/// Best-effort Redis cache. Every failure degrades to a miss so the landing page keeps
/// serving straight off PostgreSQL when the cache is down.
/// </summary>
public sealed class CacheStore(IConnectionMultiplexer? redis, TimeSpan ttl, ILogger<CacheStore> logger)
{
    public const string LeadCountKey = "leads:count";

    // Same shape the API returns, so cached payloads stay readable in redis-cli.
    private static readonly JsonSerializerOptions SerializerOptions = JsonSerializerOptions.Web;

    public bool Enabled => redis is not null;

    public async Task<T?> ReadAsync<T>(string key) where T : class
    {
        if (redis is null) return null;
        try
        {
            var raw = await redis.GetDatabase().StringGetAsync(key);
            return raw.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>(raw.ToString(), SerializerOptions);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Cache read failed for {Key}", key);
            return null;
        }
    }

    public async Task WriteAsync<T>(string key, T value)
    {
        if (redis is null) return;
        try
        {
            await redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value, SerializerOptions), ttl);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Cache write failed for {Key}", key);
        }
    }

    public async Task DropAsync(string key)
    {
        if (redis is null) return;
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Cache drop failed for {Key}", key);
        }
    }

    public async Task PingAsync()
    {
        if (redis is null) throw new InvalidOperationException("REDIS_URL is not configured");
        await redis.GetDatabase().PingAsync();
    }
}
