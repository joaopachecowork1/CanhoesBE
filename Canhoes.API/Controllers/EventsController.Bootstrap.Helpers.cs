using Canhoes.Api.Models;
using Canhoes.Api.DTOs;
using Microsoft.EntityFrameworkCore;

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
        var feedPosts = preloadedPosts ?? await _db.HubPosts
            .AsNoTracking()
            .Where(x => x.EventId == eventId && postIds.Contains(x.Id))
            .ToListAsync(ct);

        if (feedPosts.Count == 0) return [];

        var postAuthorsLookup = await _db.Users
            .AsNoTracking()
            .Where(x => feedPosts.Select(p => p.AuthorUserId).Distinct().Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.Email : x.DisplayName!, ct);

        return feedPosts.Select(post => new EventFeedPostFullDto(
            post.Id,
            post.EventId,
            post.AuthorUserId.ToString(),
            postAuthorsLookup.TryGetValue(post.AuthorUserId, out var name) ? name : "Unknown",
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
