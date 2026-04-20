namespace Canhoes.Api.Caching;

public static class CacheKeys
{
    public static string ForEvent(string key, string eventId) => $"{key}:{eventId}";
}
