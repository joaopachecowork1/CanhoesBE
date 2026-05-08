namespace Canhoes.Api.Constants;

public static class FeedConstants
{
    public const string DefaultReactionEmoji = "\u2764\uFE0F"; // Heart emoji
    public const string NotificationGroupPattern = "event_{0}"; // Used for SignalR groups
    public static readonly string[] AllowedUploadExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    public const int MaxUploadFileCount = 10;
    public const long MaxUploadSizeBytes = 25_000_000; // 25 MB
}
