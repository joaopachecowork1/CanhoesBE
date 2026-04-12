using Canhoes.Api.DTOs;
using Canhoes.Api.Media;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Canhoes.Api.Controllers;

public sealed partial class HubController
{
    [HttpGet("posts")]
    public async Task<ActionResult<List<HubPostDto>>> GetPosts([FromQuery] int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;

        var snapshot = await LoadFeedSnapshotAsync(access.EventId, take, access.UserId, ct);
        return Ok(BuildPostDtos(snapshot));
    }

    [HttpPost("posts")]
    public async Task<ActionResult<HubPostDto>> CreatePost([FromBody] CreateHubPostRequest req, CancellationToken ct = default)
    {
        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest("Text is required.");

        var mediaUrls = (req.MediaUrls ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct()
            .Take(10)
            .ToList();

        if (!string.IsNullOrWhiteSpace(req.MediaUrl) && !mediaUrls.Contains(req.MediaUrl.Trim()))
        {
            mediaUrls.Insert(0, req.MediaUrl.Trim());
        }

        mediaUrls = MediaUrlFormatter.Collect(req.MediaUrl, JsonSerializer.Serialize(mediaUrls));

        var pollQuestion = string.IsNullOrWhiteSpace(req.PollQuestion) ? null : req.PollQuestion.Trim();
        var pollOptions = (req.PollOptions ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct()
            .Take(8)
            .ToList();

        if (pollQuestion is not null)
        {
            if (pollQuestion.Length > 512) pollQuestion = pollQuestion[..512];
            if (pollOptions.Count < 2) return BadRequest("Poll must have at least 2 options.");
        }

        var post = new HubPostEntity
        {
            EventId = access.EventId,
            AuthorUserId = access.UserId,
            Text = req.Text.Trim(),
            MediaUrl = mediaUrls.FirstOrDefault(),
            MediaUrlsJson = JsonSerializer.Serialize(mediaUrls),
            IsPinned = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.HubPosts.Add(post);
        await _db.SaveChangesAsync(ct);

        await AttachOrphanMediaAsync(mediaUrls, post.Id, ct);
        var pollDto = await CreatePollAsync(post.Id, pollQuestion, pollOptions, ct);
        var authorName = await GetDisplayNameAsync(access.UserId, ct);

        return Ok(new HubPostDto
        {
            Id = post.Id,
            AuthorUserId = post.AuthorUserId.ToString(),
            AuthorName = authorName ?? "Unknown",
            Text = post.Text,
            MediaUrl = post.MediaUrl,
            MediaUrls = mediaUrls,
            IsPinned = post.IsPinned,
            CreatedAtUtc = post.CreatedAtUtc,
            LikeCount = 0,
            CommentCount = 0,
            ReactionCounts = new Dictionary<string, int>(),
            MyReactions = new List<string>(),
            LikedByMe = false,
            Poll = pollDto
        });
    }

    [HttpGet("posts/{postId}/comments")]
    public async Task<ActionResult<List<HubCommentDto>>> GetComments([FromRoute] string postId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest();

        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;
        if (!await PostExistsInActiveEventAsync(access.EventId, postId, ct)) return NotFound();

        return Ok(await LoadCommentDtosAsync(postId, access.UserId, ct));
    }

    [HttpPost("posts/{postId}/comments")]
    public async Task<ActionResult<HubCommentDto>> CreateComment([FromRoute] string postId, [FromBody] CreateHubCommentRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest("Text is required.");

        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;
        if (!await PostExistsInActiveEventAsync(access.EventId, postId, ct)) return NotFound();

        var comment = new HubPostCommentEntity
        {
            PostId = postId,
            UserId = access.UserId,
            Text = req.Text.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.HubPostComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        return Ok(await BuildCreatedCommentDtoAsync(comment, ct));
    }

    [HttpDelete("posts/{postId}/comments/{commentId}")]
    public async Task<ActionResult> DeleteOwnComment(
        [FromRoute] string postId,
        [FromRoute] string commentId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(commentId)) return BadRequest("commentId is required.");

        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;

        var comment = await _db.HubPostComments
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId, ct);
        if (comment is null) return NotFound();
        if (!await PostExistsInActiveEventAsync(access.EventId, postId, ct)) return NotFound();
        if (comment.UserId != access.UserId && !access.CanManage) return Forbid();

        var commentReactions = _db.HubPostCommentReactions.Where(x => x.CommentId == commentId);
        _db.HubPostCommentReactions.RemoveRange(commentReactions);
        _db.HubPostComments.Remove(comment);

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("posts/{postId}/comments/{commentId}/reactions")]
    public async Task<ActionResult<object>> ToggleCommentReaction(
        [FromRoute] string postId,
        [FromRoute] string commentId,
        [FromBody] ToggleReactionRequest req,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(commentId)) return BadRequest("commentId is required.");

        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;

        var comment = await _db.HubPostComments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId, ct);
        if (comment is null) return NotFound();
        if (!await PostExistsInActiveEventAsync(access.EventId, postId, ct)) return NotFound();

        var emoji = NormalizeReactionEmoji(req.Emoji);
        var existing = await _db.HubPostCommentReactions
            .SingleOrDefaultAsync(x => x.CommentId == commentId && x.UserId == access.UserId && x.Emoji == emoji, ct);

        var active = existing is null;
        if (existing is null)
        {
            _db.HubPostCommentReactions.Add(new HubPostCommentReactionEntity
            {
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

    [HttpPost("uploads")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<List<string>>> Upload([FromForm] IFormFileCollection files, CancellationToken ct = default)
    {
        if (files is null || files.Count == 0) return BadRequest("No files uploaded.");

        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;

        return Ok(await SaveUploadedFilesAsync(files, access.UserId, ct));
    }

    [HttpPost("posts/{postId}/poll/vote")]
    public async Task<ActionResult<object>> VotePoll([FromRoute] string postId, [FromBody] VotePollRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (req is null || string.IsNullOrWhiteSpace(req.OptionId)) return BadRequest("optionId is required.");

        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;

        var pollExists = await _db.HubPostPolls.AnyAsync(x => x.PostId == postId, ct)
            && await PostExistsInActiveEventAsync(access.EventId, postId, ct);
        if (!pollExists) return NotFound();

        var optionId = req.OptionId.Trim();
        var optionExists = await _db.HubPostPollOptions.AnyAsync(x => x.Id == optionId && x.PostId == postId, ct);
        if (!optionExists) return BadRequest("Invalid optionId.");

        var existing = await _db.HubPostPollVotes.SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == access.UserId, ct);
        if (existing is null)
        {
            _db.HubPostPollVotes.Add(new HubPostPollVoteEntity
            {
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

    [HttpPost("posts/{postId}/like")]
    public async Task<ActionResult<object>> ToggleLike([FromRoute] string postId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;
        if (!await PostExistsInActiveEventAsync(access.EventId, postId, ct)) return NotFound();

        var existing = await _db.HubPostReactions
            .SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == access.UserId && x.Emoji == DefaultReactionEmoji, ct);

        var liked = existing is null;
        if (existing is null)
        {
            _db.HubPostReactions.Add(new HubPostReactionEntity
            {
                PostId = postId,
                UserId = access.UserId,
                Emoji = DefaultReactionEmoji,
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

    [HttpPost("posts/{postId}/downvote")]
    public async Task<ActionResult<object>> ToggleDownvote([FromRoute] string postId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;
        if (!await PostExistsInActiveEventAsync(access.EventId, postId, ct)) return NotFound();

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

    [HttpPost("posts/{postId}/reactions")]
    public async Task<ActionResult<object>> ToggleReaction([FromRoute] string postId, [FromBody] ToggleReactionRequest req, CancellationToken ct = default)
    {
        return await ToggleReactionInternal(postId, NormalizeReactionEmoji(req.Emoji), ct);
    }

    private async Task<ActionResult<object>> ToggleReactionInternal(string postId, string emoji, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (access, error) = await RequireFeedAccessAsync(ct);
        if (error is not null) return error;
        if (!await PostExistsInActiveEventAsync(access.EventId, postId, ct)) return NotFound();

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
}
