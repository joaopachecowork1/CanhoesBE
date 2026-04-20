using Microsoft.Extensions.Caching.Memory;

namespace Canhoes.Api.Caching;

public static class MemoryCacheExtensions
{
    public static T GetOrCreate<T>(this IMemoryCache cache, string key, TimeSpan ttl, Func<ICacheEntry, T> factory)
    {
        return cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            return factory(entry);
        })!;
    }

    public static Task<T> GetOrCreateAsync<T>(this IMemoryCache cache, string key, TimeSpan ttl, Func<ICacheEntry, Task<T>> factory)
    {
        return cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            return await factory(entry);
        })!;
    }

    public static void RemoveMany(this IMemoryCache cache, params string[] keys)
    {
        foreach (var key in keys)
        {
            cache.Remove(key);
        }
    }
}
