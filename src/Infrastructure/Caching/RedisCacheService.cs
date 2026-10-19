using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MultiVendorPlatform.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly IDatabase? _database;
    private readonly bool _useRedis;

    public RedisCacheService(
        IConfiguration configuration,
        IMemoryCache memoryCache,
        ILogger<RedisCacheService> logger,
        IConnectionMultiplexer? redis = null)
    {
        _memoryCache = memoryCache;
        _logger = logger;
        _redis = redis;

        if (_redis != null && _redis.IsConnected)
        {
            _database = _redis.GetDatabase();
            _useRedis = true;
            _logger.LogInformation("Redis cache initialized and connected.");
        }
        else
        {
            _useRedis = false;
            _logger.LogWarning("Redis not connected. Falling back to In-Memory cache.");
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            if (_useRedis && _database != null)
            {
                var value = await _database.StringGetAsync(key);
                if (value.IsNullOrEmpty) return default;

                return JsonSerializer.Deserialize<T>(value!, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            return _memoryCache.TryGetValue<T>(key, out var memVal) ? memVal : default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache key {Key}. Falling back to default.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var ttl = expiry ?? TimeSpan.FromMinutes(30);

        try
        {
            var json = JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (_useRedis && _database != null)
            {
                await _database.StringSetAsync(key, json, ttl);
            }
            else
            {
                _memoryCache.Set(key, value, ttl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set cache key {Key}.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            if (_useRedis && _database != null)
            {
                await _database.KeyDeleteAsync(key);
            }
            else
            {
                _memoryCache.Remove(key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache key {Key}.", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            if (_useRedis && _redis != null)
            {
                var endpoints = _redis.GetEndPoints();
                foreach (var ep in endpoints)
                {
                    var server = _redis.GetServer(ep);
                    if (server.IsConnected)
                    {
                        var keys = server.Keys(pattern: $"{prefix}*").ToArray();
                        if (keys.Any() && _database != null)
                        {
                            await _database.KeyDeleteAsync(keys);
                        }
                    }
                }
            }
            else
            {
                // In memory fallback: prefix invalidation clears memory cache
                if (_memoryCache is MemoryCache mem)
                {
                    mem.Compact(1.0); // Simple fallback eviction
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache prefix {Prefix}.", prefix);
        }
    }

    public async Task<bool> AcquireLockAsync(string lockKey, string lockValue, TimeSpan expiry)
    {
        if (_useRedis && _database != null)
        {
            return await _database.LockTakeAsync(lockKey, lockValue, expiry);
        }
        return true; // Single-instance memory fallback
    }

    public async Task<bool> ReleaseLockAsync(string lockKey, string lockValue)
    {
        if (_useRedis && _database != null)
        {
            return await _database.LockReleaseAsync(lockKey, lockValue);
        }
        return true;
    }
}
