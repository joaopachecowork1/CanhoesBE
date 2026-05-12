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

    public FeedController(
        IFeedService feedService,
        CanhoesDbContext db, 
        IHubContext<Canhoes.Api.Hubs.EventHub> hub) 
        : base(db, hub: hub) 
    { 
        _feedService = feedService;
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

        var result = await _feedService.ToggleReactionAsync(eventId, postId, userAccess.UserId, reactionEmoji, ct);
        if (result is null) return NotFound();

        return Ok(result);
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

        var commentDtos = await _feedService.GetCommentsAsync(eventId, postId, userAccess.UserId, ct);
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

        var deleted = await _feedService.DeleteCommentAsync(eventId, postId, commentId, userAccess.UserId, userAccess.IsAdmin, ct);
        if (!deleted) return NotFound();

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

        var reactionEmoji = NormalizeFeedReactionEmoji(reactionRequest?.Emoji);
        var result = await _feedService.ToggleCommentReactionAsync(eventId, postId, commentId, userAccess.UserId, reactionEmoji, ct);
        if (result is null) return NotFound();

        return Ok(result);
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

        var result = await _feedService.VotePollAsync(eventId, postId, userAccess.UserId, voteRequest.OptionId.Trim(), ct);
        if (result is null) return NotFound();

        await NotifyPollVotedAsync(eventId, postId, voteRequest.OptionId.Trim(), ct);

        return Ok(result);
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

        var pinned = await _feedService.TogglePinAsync(eventId, postId, isAdmin: true, ct);
        if (!pinned) return NotFound();

        return Ok(new { pinned });
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

        var deleted = await _feedService.DeletePostAsync(eventId, postId, isAdmin: true, ct);
        if (!deleted) return NotFound();

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
                Id = Guid.NewGuid().ToString("N"),
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
