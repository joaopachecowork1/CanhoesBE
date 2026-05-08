using Canhoes.Api.Data;
using System.Text.Json;
using Canhoes.Api.Access;
using Canhoes.Api.DTOs;
using Canhoes.Api.Media;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Canhoes.Api.Services;
using Canhoes.Api.Auth;
using static Canhoes.Api.Mappers.EventMappers;

namespace Canhoes.Api.Controllers;

[ApiController]
public class FeedController : EventControllerBase
{
    private readonly IFeedService _feedService;
    private readonly IHubContext<Canhoes.Api.Hubs.EventHub> _hub;

    public FeedController(
        IFeedService feedService,
        CanhoesDbContext db, 
        IHubContext<Canhoes.Api.Hubs.EventHub> hub) 
        : base(db) 
    { 
        _feedService = feedService;
        _hub = hub; 
    }

    private const string DefaultFeedReactionEmoji = "\u2764\uFE0F";

    private static string NormalizeFeedReactionEmoji(string? emoji)
    {
        var normalizedValue = string.IsNullOrWhiteSpace(emoji) ? DefaultFeedReactionEmoji : emoji.Trim();
        return normalizedValue.Length > 16 ? normalizedValue[..16] : normalizedValue;
    }

    // ========================================================================
    // FEED INTERACTION ENDPOINTS (replaces HubController functionality)
    // ========================================================================

    /// <summary>
    /// Toggles a like reaction on a feed post.
    /// Mutually exclusive with downvotes.
    /// </summary>
    [HttpPost("{eventId}/feed/posts/{postId}/like")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("standard")]
    public async Task<ActionResult<object>> ToggleFeedLike([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var result = await _feedService.ToggleLikeAsync(eventId, postId, userAccess.UserId, ct);
        if (result is null) return NotFound();

        // Note: Notifications are still handled here for now to keep Hub context in controller
        // but we could move them to service if we inject IHubContext there.
        dynamic d = result;
        await NotifyFeedPostLikedAsync(eventId, postId, d.liked, userAccess.UserId, ct);
        if (d.removedDownvote) await NotifyFeedPostDownvotedAsync(eventId, postId, false, userAccess.UserId, ct);

        return Ok(result);
    }

    /// <summary>
    /// Toggles a downvote on a feed post.
    /// Mutually exclusive with likes.
    /// </summary>
    [HttpPost("{eventId}/feed/posts/{postId}/downvote")]
    public async Task<ActionResult<object>> ToggleFeedDownvote([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var result = await _feedService.ToggleDownvoteAsync(eventId, postId, userAccess.UserId, ct);
        if (result is null) return NotFound();

        dynamic d = result;
        await NotifyFeedPostDownvotedAsync(eventId, postId, d.downvoted, userAccess.UserId, ct);
        if (d.removedLike) await NotifyFeedPostLikedAsync(eventId, postId, false, userAccess.UserId, ct);

        return Ok(result);
    }

    private async Task NotifyFeedPostLikedAsync(string eventId, string postId, bool isLiked, Guid userId, CancellationToken ct)
    {
        await _hub.Clients.Group($"event_{eventId}")
            .SendAsync("PostLiked", new { postId, liked = isLiked, userId }, ct);
    }

    private async Task NotifyFeedPostDownvotedAsync(string eventId, string postId, bool isDownvoted, Guid userId, CancellationToken ct)
    {
        await _hub.Clients.Group($"event_{eventId}")
            .SendAsync("PostDownvoted", new { postId, downvoted = isDownvoted, userId }, ct);
    }

    /// <summary>
    /// Toggles a specific emoji reaction on a feed post.
    /// </summary>
    [HttpPost("{eventId}/feed/posts/{postId}/reactions")]
    public async Task<ActionResult<object>> ToggleFeedReaction([FromRoute] string eventId, [FromRoute] string postId, [FromBody] ToggleEventFeedReactionRequest? req, CancellationToken ct)
    {
        return await ToggleFeedReactionInternalAsync(eventId, postId, NormalizeFeedReactionEmoji(req?.Emoji), ct);
    }

    private async Task<ActionResult<object>> ToggleFeedReactionInternalAsync(string eventId, string postId, string reactionEmoji, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var isPostExisting = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (!isPostExisting) return NotFound();

        var existingReaction = await _db.HubPostReactions
            .SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == userAccess.UserId && x.Emoji == reactionEmoji, ct);

        var isNowActive = existingReaction is null;
        if (isNowActive)
        {
            await AddPostEmojiReactionAsync(postId, userAccess.UserId, reactionEmoji);
        }
        else
        {
            _db.HubPostReactions.Remove(existingReaction!);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { emoji = reactionEmoji, active = isNowActive });
    }

    private async Task AddPostEmojiReactionAsync(string postId, Guid userId, string emoji)
    {
        await _db.HubPostReactions.AddAsync(new HubPostReactionEntity
        {
            PostId = postId,
            UserId = userId,
            Emoji = emoji,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Retrieves all comments for a specific feed post.
    /// </summary>
    [HttpGet("{eventId}/feed/posts/{postId}/comments")]
    [ProducesResponseType(typeof(List<HubCommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<HubCommentDto>>> GetFeedComments([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest();

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var commentDtos = await LoadFeedCommentDtosAsync(postId, userAccess.UserId, ct);
        if (commentDtos.Count == 0)
        {
            var isPostExisting = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);
            if (!isPostExisting) return NotFound();
        }

        return Ok(commentDtos);
    }

    /// <summary>
    /// Adds a new comment to a feed post.
    /// </summary>
    [HttpPost("{eventId}/feed/posts/{postId}/comments")]
    public async Task<ActionResult<HubCommentDto>> CreateFeedComment([FromRoute] string eventId, [FromRoute] string postId, [FromBody] CreateEventFeedCommentRequest commentRequest, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(commentRequest?.Text)) return BadRequest("Text is required.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var comment = await _feedService.CreateCommentAsync(eventId, postId, userAccess.UserId, commentRequest.Text, ct);
        if (comment is null) return NotFound();

        await NotifyCommentCreatedAsync(eventId, postId, comment, ct);

        return Ok(comment);
    }

    private async Task NotifyCommentCreatedAsync(string eventId, string postId, HubCommentDto commentDto, CancellationToken ct)
    {
        await _hub.Clients.Group($"event_{eventId}")
            .SendAsync("CommentCreated", new { postId, comment = commentDto }, ct);
    }

    /// <summary>
    /// Deletes a specific comment from a feed post.
    /// </summary>
    [HttpDelete("{eventId}/feed/posts/{postId}/comments/{commentId}")]
    public async Task<ActionResult> DeleteFeedComment([FromRoute] string eventId, [FromRoute] string postId, [FromRoute] string commentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(commentId)) return BadRequest("commentId is required.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var targetComment = await _db.HubPostComments
            .Include(x => x.Post)
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId && x.Post.EventId == eventId, ct);
        if (targetComment is null) return NotFound();
        
        var canUserDeleteComment = targetComment.UserId == userAccess.UserId || userAccess.IsAdmin;
        if (!canUserDeleteComment) return Forbid();

        _db.HubPostComments.Remove(targetComment);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Toggles an emoji reaction on a comment.
    /// </summary>
    [HttpPost("{eventId}/feed/posts/{postId}/comments/{commentId}/reactions")]
    public async Task<ActionResult<object>> ToggleFeedCommentReaction([FromRoute] string eventId, [FromRoute] string postId, [FromRoute] string commentId, [FromBody] ToggleEventFeedReactionRequest? reactionRequest, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(commentId)) return BadRequest("commentId is required.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var isCommentValid = await _db.HubPostComments
            .AsNoTracking()
            .AnyAsync(x => x.Id == commentId && x.PostId == postId && x.Post.EventId == eventId, ct);
        if (!isCommentValid) return NotFound();

        var reactionEmoji = NormalizeFeedReactionEmoji(reactionRequest?.Emoji);
        var existingReaction = await _db.HubPostCommentReactions
            .SingleOrDefaultAsync(x => x.CommentId == commentId && x.UserId == userAccess.UserId && x.Emoji == reactionEmoji, ct);

        var isNowActive = existingReaction is null;
        if (isNowActive)
        {
            await AddCommentReactionAsync(commentId, userAccess.UserId, reactionEmoji);
        }
        else
        {
            _db.HubPostCommentReactions.Remove(existingReaction!);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { emoji = reactionEmoji, active = isNowActive });
    }

    private async Task AddCommentReactionAsync(string commentId, Guid userId, string emoji)
    {
        await _db.HubPostCommentReactions.AddAsync(new HubPostCommentReactionEntity
        {
            Id = Guid.NewGuid().ToString(),
            CommentId = commentId,
            UserId = userId,
            Emoji = emoji,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Casts a vote in a feed poll associated with a post.
    /// </summary>
    [HttpPost("{eventId}/feed/posts/{postId}/poll/vote")]
    public async Task<ActionResult<object>> VoteFeedPoll([FromRoute] string eventId, [FromRoute] string postId, [FromBody] VoteEventFeedPollRequest voteRequest, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (voteRequest is null || string.IsNullOrWhiteSpace(voteRequest.OptionId)) return BadRequest("optionId is required.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var isPollExisting = await _db.HubPostPolls.AsNoTracking().AnyAsync(x => x.PostId == postId, ct);
        var isPostExisting = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);

        if (!isPollExisting || !isPostExisting) return NotFound();

        var normalizedOptionId = voteRequest.OptionId.Trim();
        var isOptionValid = await _db.HubPostPollOptions.AsNoTracking().AnyAsync(x => x.Id == normalizedOptionId && x.PostId == postId, ct);
        if (!isOptionValid) return BadRequest("Invalid optionId.");

        var existingPollVote = await _db.HubPostPollVotes.SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == userAccess.UserId, ct);
        if (existingPollVote is null)
        {
            await CreatePollVoteAsync(postId, userAccess.UserId, normalizedOptionId);
        }
        else
        {
            existingPollVote.OptionId = normalizedOptionId;
            existingPollVote.CreatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await NotifyPollVotedAsync(eventId, postId, normalizedOptionId, ct);

        return Ok(new { optionId = normalizedOptionId });
    }

    private async Task CreatePollVoteAsync(string postId, Guid userId, string optionId)
    {
        await _db.HubPostPollVotes.AddAsync(new HubPostPollVoteEntity
        {
            Id = Guid.NewGuid().ToString(),
            PostId = postId,
            UserId = userId,
            OptionId = optionId,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task NotifyPollVotedAsync(string eventId, string postId, string optionId, CancellationToken ct)
    {
        await _hub.Clients.Group($"event_{eventId}")
            .SendAsync("PollVoted", new { postId, optionId }, ct);
    }

    /// <summary>
    /// Toggles the pinned status of a feed post.
    /// </summary>
    [HttpPost("{eventId}/feed/posts/{postId}/pin")]
    public async Task<ActionResult<object>> ToggleFeedPostPin([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (accessError is not null) return accessError;

        var targetPost = await _db.HubPosts.FirstOrDefaultAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (targetPost is null) return NotFound();

        targetPost.IsPinned = !targetPost.IsPinned;
        await _db.SaveChangesAsync(ct);

        return Ok(new { pinned = targetPost.IsPinned });
    }

    /// <summary>
    /// Deletes a feed post.
    /// </summary>
    [HttpDelete("{eventId}/feed/posts/{postId}")]
    public async Task<ActionResult> DeleteFeedPost([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (accessError is not null) return accessError;

        var targetPost = await _db.HubPosts.FirstOrDefaultAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (targetPost is null) return NotFound();

        _db.HubPosts.Remove(targetPost);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Uploads images for use in a feed post.
    /// </summary>
    [HttpPost("{eventId}/feed/uploads")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<List<string>>> UploadFeedImages([FromRoute] string eventId, IFormFileCollection files, CancellationToken ct)
    {
        if (files is null || files.Count == 0) return BadRequest("No files uploaded.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        return Ok(await SaveFeedUploadedFilesAsync(files, userAccess.UserId, ct));
    }

    // ========================================================================
    // FEED SUPPORT METHODS
    // ========================================================================

    private async Task<List<HubCommentDto>> LoadFeedCommentDtosAsync(string postId, Guid userId, CancellationToken ct)
    {
        var comments = await _db.HubPostComments
            .AsNoTracking()
            .Where(x => x.PostId == postId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        if (comments.Count == 0) return new List<HubCommentDto>();

        var commentIds = comments.Select(c => c.Id).ToList();
        var allReactions = await _db.HubPostCommentReactions
            .AsNoTracking()
            .Where(x => commentIds.Contains(x.CommentId))
            .Select(x => new { x.CommentId, x.UserId, x.Emoji })
            .ToListAsync(ct);

        // Pre-group reactions by comment to avoid N+1 LINQ iterations
        var reactionsByComment = allReactions
            .GroupBy(r => r.CommentId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.Emoji)
                    .ToDictionary(x => x.Key, x => x.Count()));

        var userIds = comments.Select(c => c.UserId).Distinct().ToList();
        var usersLookupTable = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName! })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return comments.Select(comment =>
        {
            var commentReactions = reactionsByComment.TryGetValue(comment.Id, out var r)
                ? r
                : new Dictionary<string, int>();

            var myReactionsOnComment = userId == Guid.Empty
                ? new List<string>()
                : allReactions
                    .Where(r => r.CommentId == comment.Id && r.UserId == userId)
                    .Select(r => r.Emoji)
                    .Distinct()
                    .ToList();

            return new HubCommentDto(
                comment.Id,
                comment.PostId,
                comment.UserId,
                usersLookupTable.TryGetValue(comment.UserId, out var userName) ? userName : "Unknown",
                comment.Text,
                comment.CreatedAtUtc,
                commentReactions,
                myReactionsOnComment
            );
        }).ToList();
    }

    private async Task<HubCommentDto> BuildCreatedFeedCommentDtoAsync(
        HubPostCommentEntity comment,
        IReadOnlyDictionary<Guid, string> userLookup,
        CancellationToken ct)
    {
        // Try lookup first; fall back to single query only if user is not in the batch
        var authorDisplayName = userLookup.TryGetValue(comment.UserId, out var name)
            ? name
            : await _db.Users.AsNoTracking()
                .Where(u => u.Id == comment.UserId)
                .Select(u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName!)
                .SingleOrDefaultAsync(ct)
            ?? "Unknown";

        return new HubCommentDto(
            comment.Id,
            comment.PostId,
            comment.UserId,
            authorDisplayName,
            comment.Text,
            comment.CreatedAtUtc,
            new Dictionary<string, int>(),
            new List<string>());
    }

    private async Task<List<string>> SaveFeedUploadedFilesAsync(IFormFileCollection files, Guid userId, CancellationToken ct)
    {
        var webRoot = _env?.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var hubDir = Path.Combine(webRoot, "uploads", "hub");
        Directory.CreateDirectory(hubDir);

        var uploadedUrls = new List<string>();
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        foreach (var file in files.Take(10))
        {
            if (file.Length <= 0) continue;

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            if (!allowedExtensions.Contains(ext.ToLowerInvariant())) continue;

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var absPath = Path.Combine(hubDir, fileName);

            await using var input = file.OpenReadStream();
            await using var output = new FileStream(absPath, FileMode.Create);
            await input.CopyToAsync(output, ct);

            var fileUrl = $"/uploads/hub/{fileName}";
            uploadedUrls.Add(fileUrl);

            _db.HubPostMedia.Add(new HubPostMediaEntity
            {
                Id = Guid.NewGuid().ToString(),
                Url = fileUrl,
                PostId = null,
                OriginalFileName = file.FileName,
                FileSizeBytes = file.Length,
                UploadedByUserId = userId == Guid.Empty ? null : userId,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType,
                UploadedAtUtc = DateTime.UtcNow
            });
        }

        if (uploadedUrls.Count > 0) await _db.SaveChangesAsync(ct);
        return uploadedUrls;
    }
}
