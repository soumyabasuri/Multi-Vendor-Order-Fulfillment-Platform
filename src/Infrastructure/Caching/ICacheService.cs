namespace MultiVendorPlatform.Infrastructure.Caching;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}

public static class CacheKeys
{
    public const string ProductByIdPrefix = "catalog:product:";
    public const string ProductListPrefix = "catalog:products:";
    public const string CategoryListKey = "catalog:categories:all";

    public static string ProductById(Guid productId) => $"{ProductByIdPrefix}{productId}";
}
