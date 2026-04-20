using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;
using static Canhoes.Api.Controllers.EventsControllerMappers;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    private async Task<List<EventFeedPostFullDto>> BuildFeedPostDtosAsync(
        string eventId,
        Guid userId,
        List<string> postIds,
        CancellationToken ct,
        List<HubPostEntity>? preloadedPosts = null)
    {
        var posts = preloadedPosts ?? await _db.HubPosts
            .AsNoTracking()
            .Where(x => x.EventId == eventId && postIds.Contains(x.Id))
            .ToListAsync(ct);

        if (posts.Count == 0) return [];

        var authors = await _db.Users
            .AsNoTracking()
            .Where(x => posts.Select(p => p.AuthorUserId).Distinct().Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.Email : x.DisplayName!, ct);

        return posts.Select(post => new EventFeedPostFullDto(
            post.Id,
            post.EventId,
            post.AuthorUserId.ToString(),
            authors.TryGetValue(post.AuthorUserId, out var name) ? name : "Unknown",
            post.Text,
            post.MediaUrl,
            [],
            post.IsPinned,
            new DateTimeOffset(post.CreatedAtUtc, TimeSpan.Zero),
            0,
            0,
            0,
            new Dictionary<string, int>(),
            new List<string>(),
            false,
            false,
            null)).ToList();
    }
}
