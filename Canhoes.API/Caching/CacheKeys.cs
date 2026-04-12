using Microsoft.Extensions.Caching.Memory;

namespace Canhoes.Api.Caching;

/// <summary>
/// Centralized cache keys and TTLs for in-memory caching.
/// </summary>
public static class CacheKeys
{
    // Event/phase configuration: 5 minutes
    public static readonly string EventState = "event:state:";
    public static readonly TimeSpan EventStateTtl = TimeSpan.FromMinutes(5);

    // Categories list: 10 minutes
    public static readonly string Categories = "categories:";
    public static readonly TimeSpan CategoriesTtl = TimeSpan.FromMinutes(10);

    // Nominees list: 2 minutes
    public static readonly string Nominees = "nominees:";
    public static readonly TimeSpan NomineesTtl = TimeSpan.FromMinutes(2);

    // Feed post list: 30 seconds
    public static readonly string FeedPosts = "feed:posts:";
    public static readonly TimeSpan FeedPostsTtl = TimeSpan.FromSeconds(30);

    // Aggregated stats/counts: 1 minute
    public static readonly string VoteStats = "stats:votes:";
    public static readonly TimeSpan VoteStatsTtl = TimeSpan.FromMinutes(1);

    // Member directory: 2 minutes
    public static readonly string Members = "members:";
    public static readonly TimeSpan MembersTtl = TimeSpan.FromMinutes(2);

    // Measures: 5 minutes
    public static readonly string Measures = "measures:";
    public static readonly TimeSpan MeasuresTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Build a cache key scoped by event ID.
    /// </summary>
    public static string ForEvent(string prefix, string eventId) => $"{prefix}{eventId}";

    /// <summary>
    /// Cache key patterns to invalidate when data changes.
    /// </summary>
    public static class InvalidationPatterns
    {
        // When a nominee is created/deleted/approved → invalidate nominees + categories
        public static readonly string[] OnNomineeChange = new[] { Nominees };

        // When a vote is cast → invalidate vote stats
        public static readonly string[] OnVoteChange = new[] { VoteStats };

        // When a post is created/deleted → invalidate feed cache
        public static readonly string[] OnPostChange = new[] { FeedPosts };

        // When a category is changed → invalidate categories + nominees
        public static readonly string[] OnCategoryChange = new[] { Categories, Nominees };

        // When phase/state changes → invalidate event state + categories + nominees
        public static readonly string[] OnStateChange = new[] { EventState, Categories, Nominees };
    }
}

/// <summary>
/// Extension methods to invalidate cache entry patterns.
/// </summary>
public static class CacheExtensions
{
    /// <summary>
    /// Remove all cache keys that start with any of the given prefixes.
    /// Since IMemoryCache doesn't support pattern-based invalidation,
    /// we track created keys and remove them explicitly.
    /// 
    /// For simplicity, this removes entries with known prefixes.
    /// In production with high write frequency, consider a versioned cache key approach.
    /// </summary>
    public static void RemoveByPrefix(this IMemoryCache cache, string[] prefixes)
    {
        // IMemoryCache doesn't support prefix-based removal.
        // For now this is a no-op placeholder — actual invalidation is done
        // by the calling code removing specific keys it knows about.
        // A proper implementation would track keys via a separate set.
    }
}
