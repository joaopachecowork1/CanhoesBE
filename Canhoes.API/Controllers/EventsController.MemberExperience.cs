using System.Text.Json;
using Canhoes.Api.Access;
using Canhoes.Api.DTOs;
using Canhoes.Api.Media;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Canhoes.Api.Controllers.EventsControllerMappers;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    private const string DefaultFeedReactionEmoji = "\u2764\uFE0F";

    private static string NormalizeFeedReactionEmoji(string? emoji)
    {
        var value = string.IsNullOrWhiteSpace(emoji) ? DefaultFeedReactionEmoji : emoji.Trim();
        return value.Length > 16 ? value[..16] : value;
    }

    // ========================================================================
    // FEED INTERACTION ENDPOINTS (replaces HubController functionality)
    // ========================================================================

    [HttpPost("{eventId}/feed/posts/{postId}/like")]
    public async Task<ActionResult<object>> ToggleFeedLike([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        var post = await _db.HubPosts.FirstOrDefaultAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (post is null) return NotFound();

        var existing = await _db.HubPostReactions
            .SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == access.UserId && x.Emoji == DefaultFeedReactionEmoji, ct);

        var liked = existing is null;
        if (existing is null)
        {
            _db.HubPostReactions.Add(new HubPostReactionEntity
            {
                PostId = postId,
                UserId = access.UserId,
                Emoji = DefaultFeedReactionEmoji,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            _db.HubPostReactions.Remove(existing);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { liked });
    }

    [HttpPost("{eventId}/feed/posts/{postId}/downvote")]
    public async Task<ActionResult<object>> ToggleFeedDownvote([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        var postExists = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (!postExists) return NotFound();

        var existing = await _db.HubPostDownvotes
            .SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == access.UserId, ct);

        var downvoted = existing is null;
        if (existing is null)
        {
            _db.HubPostDownvotes.Add(new HubPostDownvoteEntity
            {
                PostId = postId,
                UserId = access.UserId,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            _db.HubPostDownvotes.Remove(existing);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { downvoted });
    }

    [HttpPost("{eventId}/feed/posts/{postId}/reactions")]
    public async Task<ActionResult<object>> ToggleFeedReaction([FromRoute] string eventId, [FromRoute] string postId, [FromBody] ToggleEventFeedReactionRequest? req, CancellationToken ct)
    {
        return await ToggleFeedReactionInternalAsync(eventId, postId, NormalizeFeedReactionEmoji(req?.Emoji), ct);
    }

    private async Task<ActionResult<object>> ToggleFeedReactionInternalAsync(string eventId, string postId, string emoji, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        var postExists = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (!postExists) return NotFound();

        var existing = await _db.HubPostReactions
            .SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == access.UserId && x.Emoji == emoji, ct);

        var active = existing is null;
        if (existing is null)
        {
            _db.HubPostReactions.Add(new HubPostReactionEntity
            {
                PostId = postId,
                UserId = access.UserId,
                Emoji = emoji,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            _db.HubPostReactions.Remove(existing);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { emoji, active });
    }

    [HttpGet("{eventId}/feed/posts/{postId}/comments")]
    public async Task<ActionResult<List<HubCommentDto>>> GetFeedComments([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest();

        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        var comments = await LoadFeedCommentDtosAsync(postId, access.UserId, ct);
        if (comments.Count == 0)
        {
            var postExists = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);
            if (!postExists) return NotFound();
        }

        return Ok(comments);
    }

    [HttpPost("{eventId}/feed/posts/{postId}/comments")]
    public async Task<ActionResult<HubCommentDto>> CreateFeedComment([FromRoute] string eventId, [FromRoute] string postId, [FromBody] CreateEventFeedCommentRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(req?.Text)) return BadRequest("Text is required.");

        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        var postExists = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (!postExists) return NotFound();

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == access.UserId)
            .Select(u => new { u.Id, Name = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName! })
            .SingleOrDefaultAsync(ct);

        var comment = new HubPostCommentEntity
        {
            Id = Guid.NewGuid().ToString(),
            PostId = postId,
            UserId = access.UserId,
            Text = req.Text.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.HubPostComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        var userLookup = user is not null
            ? new Dictionary<Guid, string> { { user.Id, user.Name } }
            : new Dictionary<Guid, string>();

        return Ok(await BuildCreatedFeedCommentDtoAsync(comment, userLookup, ct));
    }

    [HttpDelete("{eventId}/feed/posts/{postId}/comments/{commentId}")]
    public async Task<ActionResult> DeleteFeedComment([FromRoute] string eventId, [FromRoute] string postId, [FromRoute] string commentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(commentId)) return BadRequest("commentId is required.");

        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        var comment = await _db.HubPostComments
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId, ct);
        if (comment is null) return NotFound();
        if (comment.UserId != access.UserId && !access.IsAdmin) return Forbid();

        // Cascade delete handles comment reactions
        _db.HubPostComments.Remove(comment);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{eventId}/feed/posts/{postId}/comments/{commentId}/reactions")]
    public async Task<ActionResult<object>> ToggleFeedCommentReaction([FromRoute] string eventId, [FromRoute] string postId, [FromRoute] string commentId, [FromBody] ToggleEventFeedReactionRequest? req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(commentId)) return BadRequest("commentId is required.");

        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        var comment = await _db.HubPostComments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId, ct);
        if (comment is null) return NotFound();

        var emoji = NormalizeFeedReactionEmoji(req?.Emoji);
        var existing = await _db.HubPostCommentReactions
            .SingleOrDefaultAsync(x => x.CommentId == commentId && x.UserId == access.UserId && x.Emoji == emoji, ct);

        var active = existing is null;
        if (existing is null)
        {
            _db.HubPostCommentReactions.Add(new HubPostCommentReactionEntity
            {
                Id = Guid.NewGuid().ToString(),
                CommentId = commentId,
                UserId = access.UserId,
                Emoji = emoji,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            _db.HubPostCommentReactions.Remove(existing);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { emoji, active });
    }

    [HttpPost("{eventId}/feed/posts/{postId}/poll/vote")]
    public async Task<ActionResult<object>> VoteFeedPoll([FromRoute] string eventId, [FromRoute] string postId, [FromBody] VoteEventFeedPollRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (req is null || string.IsNullOrWhiteSpace(req.OptionId)) return BadRequest("optionId is required.");

        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        // Sequential checks to avoid DbContext threading issues
        var hasPoll = await _db.HubPostPolls.AsNoTracking().AnyAsync(x => x.PostId == postId, ct);
        var hasPost = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);

        if (!hasPoll || !hasPost) return NotFound();

        var optionId = req.OptionId.Trim();
        var optionExists = await _db.HubPostPollOptions.AsNoTracking().AnyAsync(x => x.Id == optionId && x.PostId == postId, ct);
        if (!optionExists) return BadRequest("Invalid optionId.");

        var existing = await _db.HubPostPollVotes.SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == access.UserId, ct);
        if (existing is null)
        {
            _db.HubPostPollVotes.Add(new HubPostPollVoteEntity
            {
                Id = Guid.NewGuid().ToString(),
                PostId = postId,
                UserId = access.UserId,
                OptionId = optionId,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            existing.OptionId = optionId;
            existing.CreatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { optionId });
    }

    [HttpPost("{eventId}/feed/posts/{postId}/pin")]
    public async Task<ActionResult<object>> ToggleFeedPostPin([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (access, error) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (error is not null) return error;

        var post = await _db.HubPosts.FirstOrDefaultAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (post is null) return NotFound();

        post.IsPinned = !post.IsPinned;
        await _db.SaveChangesAsync(ct);

        return Ok(new { pinned = post.IsPinned });
    }

    [HttpDelete("{eventId}/feed/posts/{postId}")]
    public async Task<ActionResult> DeleteFeedPost([FromRoute] string eventId, [FromRoute] string postId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (access, error) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (error is not null) return error;

        var post = await _db.HubPosts.FirstOrDefaultAsync(x => x.Id == postId && x.EventId == eventId, ct);
        if (post is null) return NotFound();

        // Cascade delete is configured in OnModelCreating — the database handles
        // removing reactions, comments, polls, media, etc. when the post is deleted.
        _db.HubPosts.Remove(post);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{eventId}/feed/uploads")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<List<string>>> UploadFeedImages([FromRoute] string eventId, IFormFileCollection files, CancellationToken ct)
    {
        if (files is null || files.Count == 0) return BadRequest("No files uploaded.");

        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        return Ok(await SaveFeedUploadedFilesAsync(files, access.UserId, ct));
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
        var reactions = await _db.HubPostCommentReactions
            .AsNoTracking()
            .Where(x => commentIds.Contains(x.CommentId))
            .Select(x => new { x.CommentId, x.UserId, x.Emoji })
            .ToListAsync(ct);

        // Pre-group reactions by comment to avoid N+1 LINQ iterations
        var reactionsByComment = reactions
            .GroupBy(r => r.CommentId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.Emoji)
                    .ToDictionary(x => x.Key, x => x.Count()));

        var userIds = comments.Select(c => c.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName! })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return comments.Select(comment =>
        {
            var commentReactions = reactionsByComment.TryGetValue(comment.Id, out var r)
                ? r
                : new Dictionary<string, int>();

            var myReactions = userId == Guid.Empty
                ? new List<string>()
                : reactions
                    .Where(r => r.CommentId == comment.Id && r.UserId == userId)
                    .Select(r => r.Emoji)
                    .Distinct()
                    .ToList();

            return new HubCommentDto(
                comment.Id,
                comment.PostId,
                comment.UserId,
                users.TryGetValue(comment.UserId, out var userName) ? userName : "Unknown",
                comment.Text,
                comment.CreatedAtUtc,
                commentReactions,
                myReactions
            );
        }).ToList();
    }

    private async Task<HubCommentDto> BuildCreatedFeedCommentDtoAsync(
        HubPostCommentEntity comment,
        IReadOnlyDictionary<Guid, string> userLookup,
        CancellationToken ct)
    {
        // Try lookup first; fall back to single query only if user is not in the batch
        var displayName = userLookup.TryGetValue(comment.UserId, out var name)
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
            displayName,
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

        var urls = new List<string>();
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        foreach (var file in files.Take(10))
        {
            if (file.Length <= 0) continue;

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            if (!allowedExtensions.Contains(ext.ToLowerInvariant())) continue;

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var abs = Path.Combine(hubDir, fileName);

            await using var input = file.OpenReadStream();
            await using var output = new FileStream(abs, FileMode.Create);
            await input.CopyToAsync(output, ct);

            var url = $"/uploads/hub/{fileName}";
            urls.Add(url);

            _db.HubPostMedia.Add(new HubPostMediaEntity
            {
                Id = Guid.NewGuid().ToString(),
                Url = url,
                PostId = null,
                OriginalFileName = file.FileName,
                FileSizeBytes = file.Length,
                UploadedByUserId = userId == Guid.Empty ? null : userId,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType,
                UploadedAtUtc = DateTime.UtcNow
            });
        }

        if (urls.Count > 0) await _db.SaveChangesAsync(ct);
        return urls;
    }

    [HttpGet("{eventId}/voting/overview")]
    public async Task<ActionResult<EventVotingOverviewDto>> GetVotingOverview([FromRoute] string eventId, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (error is not null) return error;

        var votingPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        var activeCategories = await LoadActiveCategoriesAsync(eventId, ct);
        var submittedVoteCount = await CountSubmittedVotesAsync(
            access.UserId,
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

    [HttpGet("{eventId}/secret-santa/overview")]
    public async Task<ActionResult<EventSecretSantaOverviewDto>> GetSecretSantaOverview([FromRoute] string eventId, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.SecretSanta,
            ct);
        if (error is not null) return error;

        var myWishlistItemCount = await CountWishlistItemsAsync(eventId, access.UserId, ct);
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
            .FirstOrDefaultAsync(x => x.DrawId == latestDraw.Id && x.GiverUserId == access.UserId, ct);

        EventUserDto? assignedUser = null;
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

                assignedUser = new EventUserDto(receiver.Id, GetUserName(receiver), role);
                assignedWishlistItemCount = await CountWishlistItemsAsync(eventId, receiver.Id, ct);
            }
        }

        return Ok(new EventSecretSantaOverviewDto(
            eventId,
            true,
            assignment is not null && assignedUser is not null,
            latestDraw.EventCode,
            assignedUser,
            assignedWishlistItemCount,
            myWishlistItemCount
        ));
    }

    [HttpGet("{eventId}/feed/posts")]
    public async Task<ActionResult<PagedResult<EventFeedPostFullDto>>> GetPosts(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var total = await _db.HubPosts
            .CountAsync(x => x.EventId == eventId, ct);

        var posts = await _db.HubPosts
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var postIds = posts.Select(post => post.Id).ToList();

        // Load media
        var mediaByPostId = await _db.HubPostMedia
            .AsNoTracking()
            .Where(x => x.PostId != null && postIds.Contains(x.PostId))
            .OrderBy(x => x.UploadedAtUtc)
            .ToListAsync(ct);

        // Load authors
        var authorIds = posts.Select(x => x.AuthorUserId).Distinct().ToList();
        var authors = await _db.Users
            .AsNoTracking()
            .Where(x => authorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        // Load reaction counts (grouped by emoji)
        var allReactions = await _db.HubPostReactions
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .ToListAsync(ct);

        var reactionCountsByPost = allReactions
            .GroupBy(r => r.PostId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.Emoji)
                    .ToDictionary(x => x.Key, x => x.Count())
            );

        var myReactionsByPost = access.UserId == Guid.Empty
            ? new Dictionary<string, List<string>>()
            : allReactions
                .Where(r => r.UserId == access.UserId)
                .GroupBy(r => r.PostId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Emoji).Distinct().ToList());

        // Load comment counts
        var commentCounts = await _db.HubPostComments
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .GroupBy(x => x.PostId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);

        // Load like counts (heart reaction)
        var likeCounts = allReactions
            .Where(r => r.Emoji == "❤️")
            .GroupBy(r => r.PostId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Build DTOs (downvotes not supported yet)
        var likedByMe = access.UserId == Guid.Empty
            ? new HashSet<string>()
            : new HashSet<string>(
                allReactions
                    .Where(x => x.UserId == access.UserId && x.Emoji == "❤️")
                    .Select(x => x.PostId)
            );

        // Build DTOs (downvotes not supported yet)
        var dtos = posts.Select(x =>
        {
            var author = authors.TryGetValue(x.AuthorUserId, out var value) ? value : null;
            var authorName = author is null ? "Unknown" : GetUserName(author);
            
            var mediaUrls = MediaUrlFormatter.Collect(
                x.MediaUrl,
                x.MediaUrlsJson,
                mediaByPostId
                    .Where(media => media.PostId == x.Id)
                    .Select(media => media.Url)
            );

            return ToEventFeedPostFullDto(
                x,
                authorName,
                mediaUrls,
                likeCounts.GetValueOrDefault(x.Id, 0),
                commentCounts.GetValueOrDefault(x.Id, 0),
                downvoteCount: 0, // Downvotes not supported yet
                reactionCountsByPost.GetValueOrDefault(x.Id) ?? new Dictionary<string, int>(),
                myReactionsByPost.GetValueOrDefault(x.Id) ?? new List<string>(),
                likedByMe.Contains(x.Id),
                downvotedByMe: false, // Downvotes not supported yet
                poll: null // Poll support can be added later
            );
        }).ToList();

        return new PagedResult<EventFeedPostFullDto>(dtos, total, skip, take, (skip + take) < total);
    }

    [HttpPost("{eventId}/feed/posts")]
    public async Task<ActionResult<EventFeedPostDto>> CreatePost([FromRoute] string eventId, [FromBody] CreateEventPostRequest request, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest("Content is required.");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == access.UserId, ct);
        if (user is null) return Unauthorized();

        var mediaUrls = MediaUrlFormatter.Collect(request.ImageUrl, null);

        var post = new HubPostEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            AuthorUserId = access.UserId,
            Text = request.Content.Trim(),
            MediaUrl = mediaUrls.FirstOrDefault(),
            MediaUrlsJson = JsonSerializer.Serialize(mediaUrls),
            CreatedAtUtc = DateTime.UtcNow,
            IsPinned = false
        };

        _db.HubPosts.Add(post);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventFeedPostDto(post, GetUserName(user), mediaUrls));
    }

    [HttpGet("{eventId}/categories")]
    public async Task<ActionResult<PagedResult<EventCategoryDto>>> GetCategories(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (_, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var total = await _db.AwardCategories
            .CountAsync(x => x.EventId == eventId, ct);

        var categories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var dtos = categories.Select(ToEventCategoryDto).ToList();
        return new PagedResult<EventCategoryDto>(dtos, total, skip, take, (skip + take) < total);
    }

    [HttpPost("{eventId}/categories")]
    public async Task<ActionResult<EventCategoryDto>> CreateCategory([FromRoute] string eventId, [FromBody] CreateEventCategoryRequest request, CancellationToken ct)
    {
        var (_, error) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required.");
        if (!Enum.TryParse<AwardCategoryKind>(request.Kind, true, out var kind))
            return BadRequest("Invalid kind.");

        var category = await CreateCategoryEntityAsync(
            eventId,
            request.Title,
            request.Description,
            request.SortOrder,
            kind,
            ct);

        return Ok(ToEventCategoryDto(category));
    }

    [HttpGet("{eventId}/voting")]
    public async Task<ActionResult<EventVotingBoardDto>> GetVotingBoard([FromRoute] string eventId, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (error is not null) return error;

        var votingPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);

        var categories = await LoadActiveCategoriesAsync(eventId, ct);
        var categoryIds = categories.Select(x => x.Id).ToList();

        var nominees = await _db.Nominees
            .AsNoTracking()
            .Where(x =>
                x.EventId == eventId
                && x.Status == ProposalStatus.Approved
                && x.CategoryId != null
                && categoryIds.Contains(x.CategoryId))
            .OrderBy(x => x.Title)
            .ToListAsync(ct);

        var directory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var members = directory.Members;
        var users = directory.UsersById;

        var nomineeVotes = await _db.Votes
            .AsNoTracking()
            .Where(x => x.UserId == access.UserId && categoryIds.Contains(x.CategoryId))
            .ToDictionaryAsync(x => x.CategoryId, x => x.NomineeId, ct);

        var userVotes = await _db.UserVotes
            .AsNoTracking()
            .Where(x => x.VoterUserId == access.UserId && categoryIds.Contains(x.CategoryId))
            .ToDictionaryAsync(x => x.CategoryId, x => x.TargetUserId.ToString(), ct);

        var categoryDtos = categories.Select(category =>
        {
            List<EventVoteOptionDto> options;
            string? myOptionId;

            if (category.Kind == AwardCategoryKind.UserVote)
            {
                options = members
                    .Where(x => users.ContainsKey(x.UserId))
                    .OrderByDescending(x => x.Role == EventRoles.Admin)
                    .ThenBy(x => GetUserName(users[x.UserId]))
                    .Select(x => new EventVoteOptionDto(
                        x.UserId.ToString(),
                        category.Id,
                        GetUserName(users[x.UserId])
                    ))
                    .ToList();

                myOptionId = userVotes.TryGetValue(category.Id, out var selectedUserId)
                    ? selectedUserId
                    : null;
            }
            else
            {
                options = nominees
                    .Where(x => x.CategoryId == category.Id)
                    .Select(x => new EventVoteOptionDto(x.Id, category.Id, x.Title))
                    .ToList();

                myOptionId = nomineeVotes.TryGetValue(category.Id, out var selectedNomineeId)
                    ? selectedNomineeId
                    : null;
            }

            return new EventVotingCategoryDto(
                category.Id,
                category.EventId,
                category.Name,
                category.Kind.ToString(),
                category.Description,
                category.VoteQuestion,
                options,
                myOptionId
            );
        }).ToList();

        return Ok(new EventVotingBoardDto(
            eventId,
            votingPhase?.Id,
            IsPhaseOpen(votingPhase),
            categoryDtos
        ));
    }

    [HttpPost("{eventId}/votes")]
    public async Task<ActionResult<EventVoteDto>> CastVote([FromRoute] string eventId, [FromBody] CreateEventVoteRequest request, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (error is not null) return error;

        var votingPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        if (!IsPhaseOpen(votingPhase)) return BadRequest("Voting is closed.");

        var category = await _db.AwardCategories
            .FirstOrDefaultAsync(
                x => x.Id == request.CategoryId && x.EventId == eventId && x.IsActive,
                ct);

        if (category is null) return BadRequest("Invalid category.");

        if (category.Kind == AwardCategoryKind.UserVote)
        {
            if (!Guid.TryParse(request.OptionId, out var targetUserId))
                return BadRequest("Invalid option.");

            var isMember = await _db.EventMembers
                .AsNoTracking()
                .AnyAsync(x => x.EventId == eventId && x.UserId == targetUserId, ct);

            if (!isMember) return BadRequest("Invalid option.");

            var existingUserVote = await _db.UserVotes
                .FirstOrDefaultAsync(
                    x => x.CategoryId == category.Id && x.VoterUserId == access.UserId,
                    ct);

            if (existingUserVote is null)
            {
                existingUserVote = new UserVoteEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    CategoryId = category.Id,
                    VoterUserId = access.UserId,
                    TargetUserId = targetUserId,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _db.UserVotes.Add(existingUserVote);
            }
            else
            {
                existingUserVote.TargetUserId = targetUserId;
                existingUserVote.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new EventVoteDto(
                existingUserVote.Id,
                existingUserVote.VoterUserId,
                existingUserVote.CategoryId,
                existingUserVote.TargetUserId.ToString(),
                votingPhase!.Id
            ));
        }

        var nominee = await _db.Nominees
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == request.OptionId
                && x.EventId == eventId
                && x.CategoryId == category.Id
                && x.Status == ProposalStatus.Approved, ct);

        if (nominee is null) return BadRequest("Invalid option.");

        var existingVote = await _db.Votes
            .FirstOrDefaultAsync(x => x.CategoryId == category.Id && x.UserId == access.UserId, ct);

        if (existingVote is null)
        {
            existingVote = new VoteEntity
            {
                Id = Guid.NewGuid().ToString(),
                CategoryId = category.Id,
                NomineeId = nominee.Id,
                UserId = access.UserId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.Votes.Add(existingVote);
        }
        else
        {
            existingVote.NomineeId = nominee.Id;
            existingVote.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new EventVoteDto(
            existingVote.Id,
            existingVote.UserId,
            existingVote.CategoryId,
            existingVote.NomineeId,
            votingPhase!.Id
        ));
    }

    [HttpGet("{eventId}/proposals")]
    public async Task<ActionResult<PagedResult<EventProposalDto>>> GetProposals(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (_, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var total = await _db.CategoryProposals
            .CountAsync(x => x.EventId == eventId, ct);

        var dtos = await _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(ToEventProposalDto)
            .ToListAsync(ct);

        return new PagedResult<EventProposalDto>(dtos, total, skip, take, (skip + take) < total);
    }

    [HttpPost("{eventId}/proposals")]
    public async Task<ActionResult<EventProposalDto>> CreateProposal([FromRoute] string eventId, [FromBody] CreateEventProposalRequest request, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        var phase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Proposals, ct);
        if (!IsPhaseOpen(phase)) return BadRequest("Proposals are closed.");

        var proposal = new CategoryProposalEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            ProposedByUserId = access.UserId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim(),
            Status = ProposalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.CategoryProposals.Add(proposal);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventProposalDto(proposal));
    }

    [HttpPatch("{eventId}/proposals/{proposalId}")]
    public async Task<ActionResult<EventProposalDto>> UpdateProposal([FromRoute] string eventId, [FromRoute] string proposalId, [FromBody] UpdateEventProposalRequest request, CancellationToken ct)
    {
        var (_, error) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (error is not null) return error;

        var proposal = await _db.CategoryProposals
            .FirstOrDefaultAsync(x => x.Id == proposalId && x.EventId == eventId, ct);

        if (proposal is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Status)) return BadRequest("Status is required.");

        var status = NormalizeProposalStatus(request.Status);
        if (status is null)
            return BadRequest("Invalid status.");

        await ApplyCategoryProposalStatusAsync(proposal, eventId, status, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventProposalDto(proposal));
    }

    [HttpGet("{eventId}/wishlist")]
    public async Task<ActionResult<PagedResult<EventWishlistItemDto>>> GetWishlist(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (_, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Wishlist,
            ct);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var total = await _db.WishlistItems
            .CountAsync(x => x.EventId == eventId, ct);

        var dtos = await _db.WishlistItems
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(ToEventWishlistItemDto)
            .ToListAsync(ct);

        return new PagedResult<EventWishlistItemDto>(dtos, total, skip, take, (skip + take) < total);
    }

    [HttpPost("{eventId}/wishlist")]
    public async Task<ActionResult<EventWishlistItemDto>> CreateWishlistItem([FromRoute] string eventId, [FromBody] CreateEventWishlistItemRequest request, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Wishlist,
            ct);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required.");

        var item = new WishlistItemEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            UserId = access.UserId,
            Title = request.Title.Trim(),
            Url = string.IsNullOrWhiteSpace(request.Link) ? null : request.Link.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.WishlistItems.Add(item);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventWishlistItemDto(item));
    }
}
