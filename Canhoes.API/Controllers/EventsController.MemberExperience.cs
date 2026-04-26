using System.Text.Json;
using Canhoes.Api.Access;
using Canhoes.Api.DTOs;
using Canhoes.Api.Media;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using static Canhoes.Api.Controllers.EventsControllerMappers;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
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
    /// </summary>
    [HttpPost("{eventId}/feed/posts/{postId}/like")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("standard")]
    public async Task<ActionResult<object>> ToggleFeedLike([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var targetPost = await _db.HubPosts.FirstOrDefaultAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (targetPost is null) return NotFound();

        var existingReaction = await _db.HubPostReactions
            .SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == userAccess.UserId && x.Emoji == DefaultFeedReactionEmoji, ct);

        var isNowLiked = existingReaction is null;
        if (isNowLiked)
        {
            await AddPostLikeReactionAsync(postId, userAccess.UserId);
        }
        else
        {
            _db.HubPostReactions.Remove(existingReaction!);
        }

        await _db.SaveChangesAsync(ct);

        await NotifyFeedPostLikedAsync(eventId, postId, isNowLiked, ct);

        return Ok(new { liked = isNowLiked });
    }

    private async Task AddPostLikeReactionAsync(string postId, Guid userId)
    {
        await _db.HubPostReactions.AddAsync(new HubPostReactionEntity
        {
            PostId = postId,
            UserId = userId,
            Emoji = DefaultFeedReactionEmoji,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task NotifyFeedPostLikedAsync(string eventId, string postId, bool isLiked, CancellationToken ct)
    {
        await _hub.Clients.Group($"event_{eventId}")
            .SendAsync("PostLiked", new { postId, liked = isLiked }, ct);
    }

    /// <summary>
    /// Toggles a downvote on a feed post.
    /// </summary>
    [HttpPost("{eventId}/feed/posts/{postId}/downvote")]
    public async Task<ActionResult<object>> ToggleFeedDownvote([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var isPostExisting = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (!isPostExisting) return NotFound();

        var existingDownvote = await _db.HubPostDownvotes
            .SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == userAccess.UserId, ct);

        var isNowDownvoted = existingDownvote is null;
        if (isNowDownvoted)
        {
            await AddPostDownvoteAsync(postId, userAccess.UserId);
        }
        else
        {
            _db.HubPostDownvotes.Remove(existingDownvote!);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { downvoted = isNowDownvoted });
    }

    private async Task AddPostDownvoteAsync(string postId, Guid userId)
    {
        await _db.HubPostDownvotes.AddAsync(new HubPostDownvoteEntity
        {
            PostId = postId,
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        });
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

        var isPostExisting = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (!isPostExisting) return NotFound();

        var authorInfo = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userAccess.UserId)
            .Select(u => new { u.Id, Name = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName! })
            .SingleOrDefaultAsync(ct);

        var newComment = new HubPostCommentEntity
        {
            Id = Guid.NewGuid().ToString(),
            PostId = postId,
            UserId = userAccess.UserId,
            Text = commentRequest.Text.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.HubPostComments.Add(newComment);
        await _db.SaveChangesAsync(ct);

        var userLookupTable = authorInfo is not null
            ? new Dictionary<Guid, string> { { authorInfo.Id, authorInfo.Name } }
            : new Dictionary<Guid, string>();

        var createdCommentDto = await BuildCreatedFeedCommentDtoAsync(newComment, userLookupTable, ct);

        await NotifyCommentCreatedAsync(eventId, postId, createdCommentDto, ct);

        return Ok(createdCommentDto);
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

    /// <summary>
    /// Gets the voting overview for the current event cycle.
    /// </summary>
    [HttpGet("{eventId}/voting/overview")]
    public async Task<ActionResult<EventVotingOverviewDto>> GetVotingOverview([FromRoute] string eventId, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (accessError is not null) return accessError;

        var votingPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        var activeCategories = await LoadActiveCategoriesAsync(eventId, ct);
        var submittedVoteCount = await CountSubmittedVotesAsync(
            userAccess.UserId,
            activeCategories.Select(x => x.Id).ToList(),
            ct);

        return Ok(new EventVotingOverviewDto(
            eventId,
            votingPhase?.Id,
            IsPhaseOpen(votingPhase),
            votingPhase is null ? null : new DateTimeOffset(votingPhase.EndDateUtc, TimeSpan.Zero),
            activeCategories.Count,
            submittedVoteCount,
            Math.Max(0, activeCategories.Count - submittedVoteCount)
        ));
    }

    /// <summary>
    /// Gets the Secret Santa overview, including assigned user and wishlist counts.
    /// </summary>
    [HttpGet("{eventId}/secret-santa/overview")]
    public async Task<ActionResult<EventSecretSantaOverviewDto>> GetSecretSantaOverview([FromRoute] string eventId, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.SecretSanta,
            ct);
        if (accessError is not null) return accessError;

        var myWishlistItemCount = await CountWishlistItemsAsync(eventId, userAccess.UserId, ct);
        var latestDraw = await GetLatestSecretSantaDrawAsync(eventId, ct);
        if (latestDraw is null)
        {
            return Ok(new EventSecretSantaOverviewDto(
                eventId,
                false,
                false,
                null,
                null,
                0,
                myWishlistItemCount
            ));
        }

        var assignment = await _db.SecretSantaAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DrawId == latestDraw.Id && x.GiverUserId == userAccess.UserId, ct);

        EventUserDto? assignedUserDto = null;
        var assignedWishlistItemCount = 0;

        if (assignment is not null)
        {
            var receiver = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == assignment.ReceiverUserId, ct);

            if (receiver is not null)
            {
                var role = await _db.EventMembers
                    .AsNoTracking()
                    .Where(x => x.EventId == eventId && x.UserId == receiver.Id)
                    .Select(x => x.Role)
                    .FirstOrDefaultAsync(ct)
                    ?? EventRoles.User;

                assignedUserDto = new EventUserDto(receiver.Id, GetUserName(receiver), role);
                assignedWishlistItemCount = await CountWishlistItemsAsync(eventId, receiver.Id, ct);
            }
        }

        return Ok(new EventSecretSantaOverviewDto(
            eventId,
            true,
            assignment is not null && assignedUserDto is not null,
            latestDraw.EventCode,
            assignedUserDto,
            assignedWishlistItemCount,
            myWishlistItemCount
        ));
    }

    /// <summary>
    /// Retrieves a paged list of feed posts for the event. Results are cached for 30 seconds.
    /// </summary>
    [HttpGet("{eventId}/feed/posts")]
    public async Task<ActionResult<PagedResult<EventFeedPostFullDto>>> GetPosts(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;

        var validatedTake = Math.Clamp(take, 1, 200);
        var validatedSkip = Math.Max(0, skip);

        var cacheKey = $"FeedPosts_{eventId}_{userAccess.UserId}_{validatedSkip}_{validatedTake}";
        if (_cache.TryGetValue(cacheKey, out PagedResult<EventFeedPostFullDto>? cachedFeedResult))
        {
            return Ok(cachedFeedResult);
        }

        var feedPostsQuery = _db.HubPosts
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        var totalPostsCountInEvent = await feedPostsQuery.CountAsync(ct);

        var postsWithMetadata = await feedPostsQuery
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Skip(validatedSkip)
            .Take(validatedTake)
            .Select(x => new
            {
                Post = x,
                AuthorName = _db.Users
                    .Where(u => u.Id == x.AuthorUserId)
                    .Select(u => u.DisplayName ?? u.Email)
                    .FirstOrDefault(),
                LikeCount = _db.HubPostReactions.Count(r => r.PostId == x.Id && r.Emoji == "❤️"),
                CommentCount = _db.HubPostComments.Count(c => c.PostId == x.Id),
                DownvoteCount = _db.HubPostDownvotes.Count(d => d.PostId == x.Id),
                MyReactionsOnPost = _db.HubPostReactions
                    .Where(r => r.PostId == x.Id && r.UserId == userAccess.UserId)
                    .Select(r => r.Emoji)
                    .Distinct()
                    .ToList(),
                IsLikedByMe = _db.HubPostReactions.Any(r => r.PostId == x.Id && r.UserId == userAccess.UserId && r.Emoji == "❤️"),
                IsDownvotedByMe = _db.HubPostDownvotes.Any(d => d.PostId == x.Id && d.UserId == userAccess.UserId),
                MediaUrlsForPost = _db.HubPostMedia
                    .Where(m => m.PostId == x.Id)
                    .OrderBy(m => m.UploadedAtUtc)
                    .Select(m => m.Url)
                    .ToList()
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var feedPostDtos = postsWithMetadata.Select(x => ToEventFeedPostFullDto(
            x.Post,
            x.AuthorName ?? "Unknown",
            MediaUrlFormatter.Collect(x.Post.MediaUrl, x.Post.MediaUrlsJson, x.MediaUrlsForPost),
            x.LikeCount,
            x.CommentCount,
            x.DownvoteCount,
            new Dictionary<string, int>(), 
            x.MyReactionsOnPost,
            x.IsLikedByMe,
            x.IsDownvotedByMe,
            null
        )).ToList();

        var feedPagedResult = new PagedResult<EventFeedPostFullDto>(feedPostDtos, totalPostsCountInEvent, validatedSkip, validatedTake, (validatedSkip + validatedTake) < totalPostsCountInEvent);
        _cache.Set(cacheKey, feedPagedResult, TimeSpan.FromSeconds(30));

        return Ok(feedPagedResult);
    }

    /// <summary>
    /// Creates a new text or image post in the event feed.
    /// </summary>
    [HttpPost("{eventId}/feed/posts")]
    public async Task<ActionResult<EventFeedPostDto>> CreatePost([FromRoute] string eventId, [FromBody] CreateEventPostRequest createRequest, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (accessError is not null) return accessError;
        if (string.IsNullOrWhiteSpace(createRequest.Content)) return BadRequest("Content is required.");

        var currentAuthor = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userAccess.UserId, ct);
        if (currentAuthor is null) return Unauthorized();

        var collectedMediaUrlsForNewPost = MediaUrlFormatter.Collect(createRequest.ImageUrl, null);

        var newFeedPost = new HubPostEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            AuthorUserId = userAccess.UserId,
            Text = createRequest.Content.Trim(),
            MediaUrl = collectedMediaUrlsForNewPost.FirstOrDefault(),
            MediaUrlsJson = JsonSerializer.Serialize(collectedMediaUrlsForNewPost),
            CreatedAtUtc = DateTime.UtcNow,
            IsPinned = false
        };

        _db.HubPosts.Add(newFeedPost);
        await _db.SaveChangesAsync(ct);

        var createdFeedPostDto = ToEventFeedPostDto(newFeedPost, GetUserName(currentAuthor), collectedMediaUrlsForNewPost);
        
        await NotifyPostCreatedAsync(eventId, createdFeedPostDto, ct);

        return Ok(createdFeedPostDto);
    }

    private async Task NotifyPostCreatedAsync(string eventId, EventFeedPostDto postDto, CancellationToken ct)
    {
        await _hub.Clients.Group($"event_{eventId}")
            .SendAsync("PostCreated", postDto, ct);
    }

    /// <summary>
    /// Retrieves a paged list of award categories. Results are cached for 60 seconds.
    /// </summary>
    [HttpGet("{eventId}/categories")]
    public async Task<ActionResult<PagedResult<EventCategoryDto>>> GetCategories(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (accessError is not null) return accessError;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var cacheKey = $"Categories_{eventId}_{skip}_{take}";
        if (_cache.TryGetValue(cacheKey, out PagedResult<EventCategoryDto>? cachedCategories))
        {
            return Ok(cachedCategories);
        }

        var totalCategoriesCount = await _db.AwardCategories
            .CountAsync(x => x.EventId == eventId, ct);

        var awardCategoriesList = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var categoryDtosList = awardCategoriesList.Select(ToEventCategoryDto).ToList();
        var pagedCategoriesResult = new PagedResult<EventCategoryDto>(categoryDtosList, totalCategoriesCount, skip, take, (skip + take) < totalCategoriesCount);
        
        _cache.Set(cacheKey, pagedCategoriesResult, TimeSpan.FromSeconds(60));

        return Ok(pagedCategoriesResult);
    }

    /// <summary>
    /// Creates a new award category for the event.
    /// </summary>
    [HttpPost("{eventId}/categories")]
    public async Task<ActionResult<EventCategoryDto>> CreateCategory([FromRoute] string eventId, [FromBody] CreateEventCategoryRequest request, CancellationToken ct)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (accessError is not null) return accessError;
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required.");
        if (!Enum.TryParse<AwardCategoryKind>(request.Kind, true, out var kind))
            return BadRequest("Invalid kind.");

        var newCategoryEntity = await CreateCategoryEntityAsync(
            eventId,
            request.Title,
            request.Description,
            request.SortOrder,
            kind,
            ct);

        return Ok(ToEventCategoryDto(newCategoryEntity));
    }

    /// <summary>
    /// Retrieves the voting board, including all active categories and member's current selections.
    /// </summary>
    [HttpGet("{eventId}/voting")]
    public async Task<ActionResult<EventVotingBoardDto>> GetVotingBoard([FromRoute] string eventId, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (accessError is not null) return accessError;

        var cacheKey = $"VotingBoard_{eventId}_{userAccess.UserId}";
        if (_cache.TryGetValue(cacheKey, out EventVotingBoardDto? cachedVotingBoard))
        {
            return Ok(cachedVotingBoard);
        }

        var activeVotingPhaseEntity = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        var eventMemberDirectory = await LoadEventMemberDirectoryAsync(eventId, ct);

        var approvedNomineesList = await _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Status == ProposalStatus.Approved)
            .OrderBy(x => x.Title)
            .Select(n => new { n.Id, n.CategoryId, n.Title })
            .ToListAsync(ct);

        var nomineesByCategoryId = approvedNomineesList
            .GroupBy(n => n.CategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var userNomineeVotesMap = await _db.Votes
            .AsNoTracking()
            .Where(v => v.UserId == userAccess.UserId && _db.AwardCategories.Any(c => c.Id == v.CategoryId && c.EventId == eventId))
            .Select(v => new { v.CategoryId, v.NomineeId })
            .ToDictionaryAsync(x => x.CategoryId, x => x.NomineeId, ct);

        var userMemberVotesMap = await _db.UserVotes
            .AsNoTracking()
            .Where(v => v.VoterUserId == userAccess.UserId && _db.AwardCategories.Any(c => c.Id == v.CategoryId && c.EventId == eventId))
            .Select(v => new { v.CategoryId, v.TargetUserId })
            .ToDictionaryAsync(x => x.CategoryId, x => x.TargetUserId, ct);

        var activeAwardCategories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        var votingCategoryDtos = activeAwardCategories.Select(category =>
        {
            List<EventVoteOptionDto> voteOptionsList;
            string? mySelectionId;

            if (category.Kind == AwardCategoryKind.UserVote)
            {
                voteOptionsList = eventMemberDirectory.Members
                    .Where(m => eventMemberDirectory.UsersById.ContainsKey(m.UserId))
                    .OrderByDescending(m => m.Role == EventRoles.Admin)
                    .ThenBy(m => GetUserName(eventMemberDirectory.UsersById[m.UserId]))
                    .Select(m => new EventVoteOptionDto(
                        m.UserId.ToString(),
                        category.Id,
                        GetUserName(eventMemberDirectory.UsersById[m.UserId])
                    ))
                    .ToList();

                mySelectionId = userMemberVotesMap.TryGetValue(category.Id, out var targetUserId) && targetUserId != Guid.Empty 
                    ? targetUserId.ToString() 
                    : null;
            }
            else
            {
                voteOptionsList = nomineesByCategoryId.TryGetValue(category.Id, out var categoryNominees)
                    ? categoryNominees.Select(n => new EventVoteOptionDto(n.Id, category.Id, n.Title)).ToList()
                    : new List<EventVoteOptionDto>();

                mySelectionId = userNomineeVotesMap.TryGetValue(category.Id, out var nomineeId) ? nomineeId : null;
            }

            return new EventVotingCategoryDto(
                category.Id,
                category.EventId,
                category.Name,
                category.Kind.ToString(),
                category.Description,
                category.VoteQuestion,
                voteOptionsList,
                mySelectionId
            );
        }).ToList();

        var fullVotingBoard = new EventVotingBoardDto(
            eventId,
            activeVotingPhaseEntity?.Id,
            IsPhaseOpen(activeVotingPhaseEntity),
            votingCategoryDtos
        );

        _cache.Set(cacheKey, fullVotingBoard, TimeSpan.FromSeconds(30));

        return Ok(fullVotingBoard);
    }

    /// <summary>
    /// Casts or updates a vote for an award category.
    /// </summary>
    [HttpPost("{eventId}/votes")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("strict")]
    public async Task<ActionResult<EventVoteDto>> CastVote([FromRoute] string eventId, [FromBody] CreateEventVoteRequest voteRequest, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (accessError is not null) return accessError;

        var activeVotingPhaseEntity = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        if (!IsPhaseOpen(activeVotingPhaseEntity)) return BadRequest("Voting is closed.");

        var targetAwardCategory = await _db.AwardCategories
            .FirstOrDefaultAsync(
                x => x.Id == voteRequest.CategoryId && x.EventId == eventId && x.IsActive,
                ct);

        if (targetAwardCategory is null) return BadRequest("Invalid category.");

        if (targetAwardCategory.Kind == AwardCategoryKind.UserVote)
        {
            return await CastUserVoteAsync(eventId, userAccess.UserId, targetAwardCategory, voteRequest, ct);
        }

        return await CastNomineeVoteAsync(eventId, userAccess.UserId, targetAwardCategory, voteRequest, ct);
    }

    private async Task<ActionResult<EventVoteDto>> CastUserVoteAsync(string eventId, Guid userId, AwardCategoryEntity category, CreateEventVoteRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.OptionId, out var targetUserId))
            return BadRequest("Invalid option.");

        var isUserMemberOfEvent = await _db.EventMembers
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId && x.UserId == targetUserId, ct);

        if (!isUserMemberOfEvent) return BadRequest("Invalid option.");

        var existingUserVoteEntity = await _db.UserVotes
            .FirstOrDefaultAsync(
                x => x.CategoryId == category.Id && x.VoterUserId == userId,
                ct);

        if (existingUserVoteEntity is null)
        {
            existingUserVoteEntity = new UserVoteEntity
            {
                Id = Guid.NewGuid().ToString(),
                CategoryId = category.Id,
                VoterUserId = userId,
                TargetUserId = targetUserId,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.UserVotes.Add(existingUserVoteEntity);
        }
        else
        {
            existingUserVoteEntity.TargetUserId = targetUserId;
            existingUserVoteEntity.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await NotifyVoteCastAsync(eventId, category.Id, request.OptionId, ct);

        return Ok(new EventVoteDto(
            existingUserVoteEntity.Id,
            existingUserVoteEntity.VoterUserId,
            existingUserVoteEntity.CategoryId,
            existingUserVoteEntity.TargetUserId.ToString(),
            existingUserVoteEntity.UpdatedAtUtc
        ));
    }

    private async Task<ActionResult<EventVoteDto>> CastNomineeVoteAsync(string eventId, Guid userId, AwardCategoryEntity category, CreateEventVoteRequest request, CancellationToken ct)
    {
        var approvedNomineeEntity = await _db.Nominees
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == request.OptionId
                && x.EventId == eventId
                && x.CategoryId == category.Id
                && x.Status == ProposalStatus.Approved, ct);

        if (approvedNomineeEntity is null) return BadRequest("Invalid option.");

        var existingNomineeVoteEntity = await _db.Votes
            .FirstOrDefaultAsync(x => x.CategoryId == category.Id && x.UserId == userId, ct);

        if (existingNomineeVoteEntity is null)
        {
            existingNomineeVoteEntity = new VoteEntity
            {
                Id = Guid.NewGuid().ToString(),
                CategoryId = category.Id,
                NomineeId = approvedNomineeEntity.Id,
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.Votes.Add(existingNomineeVoteEntity);
        }
        else
        {
            existingNomineeVoteEntity.NomineeId = approvedNomineeEntity.Id;
            existingNomineeVoteEntity.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await NotifyVoteCastAsync(eventId, category.Id, request.OptionId, ct);

        return Ok(new EventVoteDto(
            existingNomineeVoteEntity.Id,
            existingNomineeVoteEntity.UserId,
            existingNomineeVoteEntity.CategoryId,
            existingNomineeVoteEntity.NomineeId,
            existingNomineeVoteEntity.UpdatedAtUtc
        ));
    }

    private async Task NotifyVoteCastAsync(string eventId, string categoryId, string optionId, CancellationToken ct)
    {
        await _hub.Clients.Group($"event_{eventId}")
            .SendAsync("VoteCast", new { categoryId, optionId }, ct);
    }

    /// <summary>
    /// Lists all category proposals for the event (paginated).
    /// </summary>
    [HttpGet("{eventId}/proposals")]
    [ProducesResponseType(typeof(PagedResult<EventProposalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<EventProposalDto>>> GetProposals(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (accessError is not null) return accessError;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var totalProposalsCount = await _db.CategoryProposals
            .CountAsync(x => x.EventId == eventId, ct);

        var proposalDtosList = (await _db.CategoryProposals
        .AsNoTracking()
        .Where(x => x.EventId == eventId)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Skip(skip)
        .Take(take)
        .ToListAsync(ct))
        .Select(ToEventProposalDto)
        .ToList();

        return new PagedResult<EventProposalDto>(proposalDtosList, totalProposalsCount, skip, take, (skip + take) < totalProposalsCount);
    }

    /// <summary>
    /// Submits a new category proposal.
    /// </summary>
    [HttpPost("{eventId}/proposals")]
    public async Task<ActionResult<EventProposalDto>> CreateProposal([FromRoute] string eventId, [FromBody] CreateEventProposalRequest request, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (accessError is not null) return accessError;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        var activeProposalsPhaseEntity = await GetActivePhaseAsync(eventId, EventPhaseTypes.Proposals, ct);
        if (!IsPhaseOpen(activeProposalsPhaseEntity)) return BadRequest("Proposals are closed.");

        var newCategoryProposal = new CategoryProposalEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            ProposedByUserId = userAccess.UserId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim(),
            Status = ProposalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.CategoryProposals.Add(newCategoryProposal);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventProposalDto(newCategoryProposal));
    }

    /// <summary>
    /// Updates an existing category proposal (admin only).
    /// </summary>
    [HttpPatch("{eventId}/proposals/{proposalId}")]
    public async Task<ActionResult<EventProposalDto>> UpdateProposal([FromRoute] string eventId, [FromRoute] string proposalId, [FromBody] UpdateEventProposalRequest request, CancellationToken ct)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (accessError is not null) return accessError;

        var targetCategoryProposal = await _db.CategoryProposals
            .FirstOrDefaultAsync(x => x.Id == proposalId && x.EventId == eventId, ct);

        if (targetCategoryProposal is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Status)) return BadRequest("Status is required.");

        var normalizedProposalStatus = NormalizeProposalStatus(request.Status);
        if (normalizedProposalStatus is null)
            return BadRequest("Invalid status.");

        await ApplyCategoryProposalStatusAsync(targetCategoryProposal, eventId, normalizedProposalStatus, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventProposalDto(targetCategoryProposal));
    }

    /// <summary>
    /// Retrieves a paged list of wishlist items for the event.
    /// </summary>
    [HttpGet("{eventId}/wishlist")]
    public async Task<ActionResult<PagedResult<EventWishlistItemDto>>> GetWishlist(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Wishlist,
            ct);
        if (accessError is not null) return accessError;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var totalWishlistItemsCount = await _db.WishlistItems
            .CountAsync(x => x.EventId == eventId, ct);

        var wishlistDtosList = (await _db.WishlistItems
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct))
            .Select(ToEventWishlistItemDto)
            .ToList();

        return new PagedResult<EventWishlistItemDto>(wishlistDtosList, totalWishlistItemsCount, skip, take, (skip + take) < totalWishlistItemsCount);
    }

    /// <summary>
    /// Adds a new item to the member's wishlist for the event.
    /// </summary>
    [HttpPost("{eventId}/wishlist")]
    public async Task<ActionResult<EventWishlistItemDto>> CreateWishlistItem([FromRoute] string eventId, [FromBody] CreateEventWishlistItemRequest request, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Wishlist,
            ct);
        if (accessError is not null) return accessError;
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required.");

        var newWishlistItem = new WishlistItemEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            UserId = userAccess.UserId,
            Title = request.Title.Trim(),
            Url = string.IsNullOrWhiteSpace(request.Link) ? null : request.Link.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.WishlistItems.Add(newWishlistItem);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventWishlistItemDto(newWishlistItem));
    }
}
