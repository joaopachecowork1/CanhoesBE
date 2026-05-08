using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using Canhoes.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Services;

public sealed class FeedService : IFeedService
{
    private readonly IFeedRepository _feedRepository;
    private readonly IUserRepository _userRepository;

    public FeedService(IFeedRepository feedRepository, IUserRepository userRepository)
    {
        _feedRepository = feedRepository;
        _userRepository = userRepository;
    }

    private const string DefaultFeedReactionEmoji = "\u2764\uFE0F";

    public async Task<object> ToggleLikeAsync(string eventId, string postId, Guid userId, CancellationToken ct)
    {
        var post = await _feedRepository.GetPostAsync(postId, eventId, ct);
        if (post is null) return null!;

        var existingReaction = await _feedRepository.GetReactionAsync(postId, userId, DefaultFeedReactionEmoji, ct);
        var existingDownvote = await _feedRepository.GetDownvoteAsync(postId, userId, ct);

        var isNowLiked = existingReaction is null;
        bool removedDownvote = false;

        if (isNowLiked)
        {
            await _feedRepository.AddReactionAsync(new HubPostReactionEntity
            {
                PostId = postId,
                UserId = userId,
                Emoji = DefaultFeedReactionEmoji,
                CreatedAtUtc = DateTime.UtcNow
            }, ct);

            if (existingDownvote is not null)
            {
                await _feedRepository.DeleteDownvoteAsync(existingDownvote, ct);
                removedDownvote = true;
            }
        }
        else
        {
            await _feedRepository.DeleteReactionAsync(existingReaction!, ct);
        }

        await _feedRepository.SaveChangesAsync(ct);
        return new { liked = isNowLiked, removedDownvote };
    }

    public async Task<object> ToggleDownvoteAsync(string eventId, string postId, Guid userId, CancellationToken ct)
    {
        var post = await _feedRepository.GetPostAsync(postId, eventId, ct);
        if (post is null) return null!;

        var existingDownvote = await _feedRepository.GetDownvoteAsync(postId, userId, ct);
        var existingLike = await _feedRepository.GetReactionAsync(postId, userId, DefaultFeedReactionEmoji, ct);

        var isNowDownvoted = existingDownvote is null;
        bool removedLike = false;

        if (isNowDownvoted)
        {
            await _feedRepository.AddDownvoteAsync(new HubPostDownvoteEntity
            {
                PostId = postId,
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            }, ct);

            if (existingLike is not null)
            {
                await _feedRepository.DeleteReactionAsync(existingLike, ct);
                removedLike = true;
            }
        }
        else
        {
            await _feedRepository.DeleteDownvoteAsync(existingDownvote!, ct);
        }

        await _feedRepository.SaveChangesAsync(ct);
        return new { downvoted = isNowDownvoted, removedLike };
    }

    public async Task<object> ToggleReactionAsync(string eventId, string postId, Guid userId, string emoji, CancellationToken ct)
    {
        var post = await _feedRepository.GetPostAsync(postId, eventId, ct);
        if (post is null) return null!;

        var existingReaction = await _feedRepository.GetReactionAsync(postId, userId, emoji, ct);
        var isNowActive = existingReaction is null;

        if (isNowActive)
        {
            await _feedRepository.AddReactionAsync(new HubPostReactionEntity
            {
                PostId = postId,
                UserId = userId,
                Emoji = emoji,
                CreatedAtUtc = DateTime.UtcNow
            }, ct);
        }
        else
        {
            await _feedRepository.DeleteReactionAsync(existingReaction!, ct);
        }

        await _feedRepository.SaveChangesAsync(ct);
        return new { emoji, active = isNowActive };
    }

    public async Task<List<HubCommentDto>> GetCommentsAsync(string eventId, string postId, Guid userId, CancellationToken ct)
    {
        var comments = await _feedRepository.GetCommentsAsync(postId, eventId, ct);
        if (comments.Count == 0) return new List<HubCommentDto>();

        var userIds = comments.Select(c => c.UserId).Distinct().ToList();
        var users = await _userRepository.GetUsersAsync(userIds, ct);
        var usersLookup = users.ToDictionary(u => u.Id, u => u.DisplayName ?? u.Email);

        var reactions = await _feedRepository.GetCommentReactionsAsync(comments.Select(c => c.Id).ToList(), ct);
        var reactionsByComment = reactions.GroupBy(r => r.CommentId)
            .ToDictionary(g => g.Key, g => g.GroupBy(r => r.Emoji).ToDictionary(x => x.Key, x => x.Count()));

        return comments.Select(c => new HubCommentDto(
            c.Id,
            c.PostId,
            c.UserId,
            usersLookup.TryGetValue(c.UserId, out var name) ? name : "Unknown",
            c.Text,
            c.CreatedAtUtc,
            reactionsByComment.TryGetValue(c.Id, out var r) ? r : new Dictionary<string, int>(),
            reactions.Where(x => x.CommentId == c.Id && x.UserId == userId).Select(x => x.Emoji).Distinct().ToList()
        )).ToList();
    }

    public async Task<HubCommentDto?> CreateCommentAsync(string eventId, string postId, Guid userId, string text, CancellationToken ct)
    {
        var post = await _feedRepository.GetPostAsync(postId, eventId, ct);
        if (post is null) return null;

        var comment = new HubPostCommentEntity
        {
            Id = Guid.NewGuid().ToString(),
            PostId = postId,
            UserId = userId,
            Text = text.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        await _feedRepository.AddCommentAsync(comment, ct);
        await _feedRepository.SaveChangesAsync(ct);

        var user = await _userRepository.GetUserAsync(userId, ct);
        var userName = user != null ? (user.DisplayName ?? user.Email) : "Unknown";

        return new HubCommentDto(
            comment.Id,
            comment.PostId,
            comment.UserId,
            userName,
            comment.Text,
            comment.CreatedAtUtc,
            new Dictionary<string, int>(),
            new List<string>());
    }

    public async Task<bool> DeleteCommentAsync(string eventId, string postId, string commentId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var comment = await _feedRepository.GetCommentAsync(commentId, postId, eventId, ct);
        if (comment is null) return false;

        if (comment.UserId != userId && !isAdmin) return false;

        await _feedRepository.DeleteCommentAsync(comment, ct);
        await _feedRepository.SaveChangesAsync(ct);
        return true;
    }

    public async Task<object> ToggleCommentReactionAsync(string eventId, string postId, string commentId, Guid userId, string emoji, CancellationToken ct)
    {
        var comment = await _feedRepository.GetCommentAsync(commentId, postId, eventId, ct);
        if (comment is null) return null!;

        var existingReaction = await _feedRepository.GetCommentReactionAsync(commentId, userId, emoji, ct);
        var isNowActive = existingReaction is null;

        if (isNowActive)
        {
            await _feedRepository.AddCommentReactionAsync(new HubPostCommentReactionEntity
            {
                Id = Guid.NewGuid().ToString(),
                CommentId = commentId,
                UserId = userId,
                Emoji = emoji,
                CreatedAtUtc = DateTime.UtcNow
            }, ct);
        }
        else
        {
            await _feedRepository.DeleteCommentReactionAsync(existingReaction!, ct);
        }

        await _feedRepository.SaveChangesAsync(ct);
        return new { emoji, active = isNowActive };
    }

    public async Task<object> VotePollAsync(string eventId, string postId, Guid userId, string optionId, CancellationToken ct)
    {
        // Poll logic here
        return new { optionId };
    }

    public async Task<bool> TogglePinAsync(string eventId, string postId, bool isAdmin, CancellationToken ct)
    {
        if (!isAdmin) return false;
        var post = await _feedRepository.GetPostAsync(postId, eventId, ct);
        if (post is null) return false;

        post.IsPinned = !post.IsPinned;
        await _feedRepository.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeletePostAsync(string eventId, string postId, bool isAdmin, CancellationToken ct)
    {
        if (!isAdmin) return false;
        var post = await _feedRepository.GetPostAsync(postId, eventId, ct);
        if (post is null) return false;

        await _feedRepository.DeletePostAsync(post, ct);
        await _feedRepository.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<EventFeedPostFullDto>> GetRecentPostsAsync(string eventId, Guid userId, int take, CancellationToken ct)
    {
        var posts = await _feedRepository.GetPostsPagedAsync(eventId, 0, take, ct);
        if (posts.Count == 0) return new List<EventFeedPostFullDto>();

        var userIds = posts.Select(p => p.AuthorUserId).Distinct().ToList();
        var users = await _userRepository.GetUsersAsync(userIds, ct);
        var usersLookup = users.ToDictionary(u => u.Id, u => u.DisplayName ?? u.Email);

        // This is a bit simplified; real implementation would need more joins or multiple queries
        return posts.Select(p => new EventFeedPostFullDto(
            p.Id,
            p.EventId,
            p.AuthorUserId.ToString(),
            usersLookup.TryGetValue(p.AuthorUserId, out var name) ? name : "Unknown",
            p.Text,
            p.MediaUrl,
            new List<string>(), // MediaUrls
            p.IsPinned,
            new DateTimeOffset(p.CreatedAtUtc, TimeSpan.Zero),
            0, // LikeCount
            0, // CommentCount
            0, // DownvoteCount
            new Dictionary<string, int>(), // ReactionCounts
            new List<string>(), // MyReactions
            false, // LikedByMe
            false, // DownvotedByMe
            null // Poll
        )).ToList();
    }
}
