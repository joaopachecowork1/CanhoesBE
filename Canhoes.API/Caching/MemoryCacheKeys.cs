namespace Canhoes.Api.Caching;

public static class MemoryCacheKeys
{
    public static string EventSummaries() => "events:summaries";
    public static string EventOverview(string eventId) => $"events:{eventId}:overview";
    public static string EventHomeSnapshot(string eventId) => $"events:{eventId}:home-snapshot";
    public static string EventAdminState(string eventId) => $"events:{eventId}:admin:state";
    public static string EventAdminCategories(string eventId) => $"events:{eventId}:admin:categories";
    public static string FeedSnapshot(string eventId, int take) => $"events:{eventId}:feed:snapshot:{take}";
}
