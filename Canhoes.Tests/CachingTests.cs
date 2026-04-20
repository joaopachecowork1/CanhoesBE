using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Canhoes.Api.Caching;
using Xunit;

namespace Canhoes.Tests;

public class CachingTests
{
    [Fact]
    public void MemoryCacheKeys_ShouldBeStableAndDistinct()
    {
        MemoryCacheKeys.EventSummaries().Should().Be("events:summaries");
        MemoryCacheKeys.EventOverview("abc").Should().Be("events:abc:overview");
        MemoryCacheKeys.EventHomeSnapshot("abc").Should().Be("events:abc:home-snapshot");
        MemoryCacheKeys.FeedSnapshot("abc", 50).Should().Be("events:abc:feed:snapshot:50");
    }

    [Fact]
    public void GetOrCreate_ShouldCacheValueForTtl()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var calls = 0;

        var first = cache.GetOrCreate("k", TimeSpan.FromMinutes(5), _ =>
        {
            calls++;
            return 42;
        });

        var second = cache.GetOrCreate("k", TimeSpan.FromMinutes(5), _ =>
        {
            calls++;
            return 99;
        });

        first.Should().Be(42);
        second.Should().Be(42);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldCacheAsyncValue()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var calls = 0;

        var first = await cache.GetOrCreateAsync("async", TimeSpan.FromMinutes(5), async _ =>
        {
            await Task.Yield();
            calls++;
            return "ok";
        });

        var second = await cache.GetOrCreateAsync("async", TimeSpan.FromMinutes(5), async _ =>
        {
            await Task.Yield();
            calls++;
            return "nope";
        });

        first.Should().Be("ok");
        second.Should().Be("ok");
        calls.Should().Be(1);
    }

    [Fact]
    public void RemoveMany_ShouldEvictAllKeys()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("a", 1);
        cache.Set("b", 2);

        cache.RemoveMany("a", "b");

        cache.TryGetValue("a", out _).Should().BeFalse();
        cache.TryGetValue("b", out _).Should().BeFalse();
    }
}
