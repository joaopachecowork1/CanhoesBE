using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Data;
using Canhoes.Api.DTOs;
using Canhoes.Api.Media;
using Canhoes.Api.Models;

namespace Canhoes.Api.Controllers;

[ApiController]
[Route("api/hub")]
[Authorize]
public sealed class HubController : ControllerBase
{
    private readonly CanhoesDbContext _db;
    private readonly IWebHostEnvironment _env;

    private sealed record ActiveFeedAccessContext(
        string EventId,
        Guid UserId,
        bool IsAdmin,
        bool IsMember,
        EventModuleAccessSnapshot ModuleAccess)
    {
        public bool CanAccess => IsAdmin || IsMember;
        public bool CanManage => IsAdmin;
    }

    private static readonly HashSet<string> AllowedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    public HubController(CanhoesDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    // ------------------------------
    // Public feed
    // ------------------------------

    
    [HttpGet("posts")]
    public async Task<ActionResult<List<HubPostDto>>> GetPosts([FromQuery] int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);
        if (error is not null) return error;

        var posts = await _db.HubPosts
            .AsNoTracking()
            .Where(x => x.EventId == access.EventId)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);

        var postIds = posts.Select(p => p.Id).ToList();
        var mediaByPostId = await _db.HubPostMedia
            .AsNoTracking()
            .Where(x => x.PostId != null && postIds.Contains(x.PostId))
            .OrderBy(x => x.UploadedAtUtc)
            .ToListAsync(ct);

        var commentCounts = await _db.HubPostComments
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .GroupBy(x => x.PostId)
            .Select(g => new { PostId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PostId, x => x.Count, ct);

        // Reactions
        var reactions = await _db.HubPostReactions
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .Select(x => new { x.PostId, x.UserId, x.Emoji })
            .ToListAsync(ct);

        var reactionCounts = reactions
            .GroupBy(r => (r.PostId, r.Emoji))
            .ToDictionary(g => g.Key, g => g.Count());

        var myReactions = access.UserId == Guid.Empty
            ? new Dictionary<string, List<string>>()
            : reactions
                .Where(r => r.UserId == access.UserId)
                .GroupBy(r => r.PostId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Emoji).Distinct().ToList());

        // Authors
        var authorIds = posts.Select(p => p.AuthorUserId).Distinct().ToList();
        var authors = await _db.Users
            .AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.Email : x.DisplayName!, ct);

        // Polls
        var polls = await _db.HubPostPolls
            .AsNoTracking()
            .Where(p => postIds.Contains(p.PostId))
            .ToListAsync(ct);

        var pollOptions = await _db.HubPostPollOptions
            .AsNoTracking()
            .Where(o => postIds.Contains(o.PostId))
            .OrderBy(o => o.SortOrder)
            .ToListAsync(ct);

        var pollVotes = await _db.HubPostPollVotes
            .AsNoTracking()
            .Where(v => postIds.Contains(v.PostId))
            .Select(v => new { v.PostId, v.UserId, v.OptionId })
            .ToListAsync(ct);

        var myPollVote = access.UserId == Guid.Empty
            ? new Dictionary<string, string?>()
            : pollVotes
                .Where(v => v.UserId == access.UserId)
                .GroupBy(v => v.PostId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.OptionId).FirstOrDefault());

        var pollCounts = pollVotes
            .GroupBy(v => v.OptionId)
            .ToDictionary(g => g.Key, g => g.Count());

        var dtos = posts.Select(p =>
        {
            var media = MediaUrlFormatter.Collect(
                p.MediaUrl,
                p.MediaUrlsJson,
                mediaByPostId
                    .Where(mediaRecord => mediaRecord.PostId == p.Id)
                    .Select(mediaRecord => mediaRecord.Url)
            );

            HubPollDto? pollDto = null;
            var poll = polls.FirstOrDefault(x => x.PostId == p.Id);
            if (poll is not null)
            {
                var opts = pollOptions.Where(o => o.PostId == p.Id).ToList();
                var myOptId = myPollVote.TryGetValue(p.Id, out var v) ? v : null;
                var optionDtos = opts.Select(o => new HubPollOptionDto
                {
                    Id = o.Id,
                    Text = o.Text,
                    VoteCount = pollCounts.TryGetValue(o.Id, out var c) ? c : 0
                }).ToList();
                var total = optionDtos.Sum(x => x.VoteCount);
                pollDto = new HubPollDto
                {
                    Question = poll.Question,
                    Options = optionDtos,
                    MyOptionId = myOptId,
                    TotalVotes = total
                };
            }

            var counts = reactions
                .Where(r => r.PostId == p.Id)
                .GroupBy(r => r.Emoji)
                .ToDictionary(g => g.Key, g => g.Count());

            var mine = myReactions.TryGetValue(p.Id, out var mr) ? mr : new List<string>();
            var likedByMe = mine.Contains("â¤ï¸");
            var likeCount = counts.TryGetValue("â¤ï¸", out var lc) ? lc : 0;

            return new HubPostDto
            {
                Id = p.Id,
                AuthorUserId = p.AuthorUserId.ToString(),
                AuthorName = authors.TryGetValue(p.AuthorUserId, out var n) ? n : "Unknown",
                Text = p.Text,
                MediaUrl = media.FirstOrDefault(),
                MediaUrls = media,
                IsPinned = p.IsPinned,
                CreatedAtUtc = p.CreatedAtUtc,
                LikeCount = likeCount,
                CommentCount = commentCounts.TryGetValue(p.Id, out var cc) ? cc : 0,
                ReactionCounts = counts,
                MyReactions = mine,
                LikedByMe = likedByMe,
                Poll = pollDto
            };
        }).ToList();

        return Ok(dtos);
    }


    
    [HttpPost("posts")]
    public async Task<ActionResult<HubPostDto>> CreatePost([FromBody] CreateHubPostRequest req, CancellationToken ct = default)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest("Text is required.");

        var mediaUrls = (req.MediaUrls ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct()
            .Take(10)
            .ToList();

        // Backwards compatible single URL
        if (!string.IsNullOrWhiteSpace(req.MediaUrl) && !mediaUrls.Contains(req.MediaUrl.Trim()))
            mediaUrls.Insert(0, req.MediaUrl.Trim());
        mediaUrls = MediaUrlFormatter.Collect(req.MediaUrl, JsonSerializer.Serialize(mediaUrls));

        // Optional poll (single choice)
        var pollQuestion = string.IsNullOrWhiteSpace(req.PollQuestion) ? null : req.PollQuestion.Trim();
        var pollOptions = (req.PollOptions ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
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
            // Always persist a JSON array so we never depend on DB nullability/default constraints.
            MediaUrlsJson = JsonSerializer.Serialize(mediaUrls),
            IsPinned = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.HubPosts.Add(post);
        await _db.SaveChangesAsync(ct);

        // Associate any previously-uploaded media records (PostId == null) with this post.
        if (mediaUrls.Count > 0)
        {
            var orphanMedia = await _db.HubPostMedia
                .Where(m => mediaUrls.Contains(m.Url) && m.PostId == null)
                .ToListAsync(ct);

            foreach (var m in orphanMedia)
                m.PostId = post.Id;

            if (orphanMedia.Count > 0)
                await _db.SaveChangesAsync(ct);
        }

        // Create poll rows after we have a post id
        HubPollDto? pollDto = null;
        if (pollQuestion is not null)
        {
            _db.HubPostPolls.Add(new HubPostPollEntity
            {
                PostId = post.Id,
                Question = pollQuestion,
                CreatedAtUtc = DateTime.UtcNow
            });

            var optionEntities = pollOptions
                .Select((t, i) => new HubPostPollOptionEntity
                {
                    PostId = post.Id,
                    Text = t.Length > 256 ? t[..256] : t,
                    SortOrder = i
                })
                .ToList();

            _db.HubPostPollOptions.AddRange(optionEntities);
            await _db.SaveChangesAsync(ct);

            pollDto = new HubPollDto
            {
                Question = pollQuestion,
                Options = optionEntities
                    .Select(o => new HubPollOptionDto { Id = o.Id, Text = o.Text, VoteCount = 0 })
                    .ToList(),
                MyOptionId = null,
                TotalVotes = 0
            };
        }

        // return DTO for convenience (minimal fields)
        var me = await _db.Users.AsNoTracking().Where(u => u.Id == access.UserId)
            .Select(u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName!)
            .SingleOrDefaultAsync(ct);

        return Ok(new HubPostDto
        {
            Id = post.Id,
            AuthorUserId = post.AuthorUserId.ToString(),
            AuthorName = me ?? "Unknown",
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
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);
        if (error is not null) return error;

        var postExists = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == access.EventId, ct);
        if (!postExists) return NotFound();

        var comments = await _db.HubPostComments
            .AsNoTracking()
            .Where(x => x.PostId == postId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var commentIds = comments.Select(c => c.Id).ToList();

        var reactions = await _db.HubPostCommentReactions
            .AsNoTracking()
            .Where(x => commentIds.Contains(x.CommentId))
            .Select(x => new { x.CommentId, x.UserId, x.Emoji })
            .ToListAsync(ct);

        var userIds = comments.Select(c => c.UserId).Distinct().ToList();
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.Email : x.DisplayName!, ct);

        var dtos = comments.Select(c => new HubCommentDto(
            c.Id,
            c.PostId,
            c.UserId,
            users.TryGetValue(c.UserId, out var n) ? n : "Unknown",
            c.Text,
            c.CreatedAtUtc,
            reactions
                .Where(r => r.CommentId == c.Id)
                .GroupBy(r => r.Emoji)
                .ToDictionary(g => g.Key, g => g.Count()),
            access.UserId == Guid.Empty
                ? new List<string>()
                : reactions
                    .Where(r => r.CommentId == c.Id && r.UserId == access.UserId)
                    .Select(r => r.Emoji)
                    .Distinct()
                    .ToList()
        )).ToList();

        return Ok(dtos);
    }

    [HttpPost("posts/{postId}/comments")]
    public async Task<ActionResult<HubCommentDto>> CreateComment([FromRoute] string postId, [FromBody] CreateHubCommentRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest("Text is required.");

        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);
        if (error is not null) return error;

        var postExists = await _db.HubPosts.AnyAsync(x => x.Id == postId && x.EventId == access.EventId, ct);
        if (!postExists) return NotFound();

        var comment = new HubPostCommentEntity
        {
            PostId = postId,
            UserId = access.UserId,
            Text = req.Text.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.HubPostComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        var me = await _db.Users.AsNoTracking().Where(u => u.Id == access.UserId)
            .Select(u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName!)
            .SingleOrDefaultAsync(ct);

        return Ok(new HubCommentDto(
            comment.Id,
            comment.PostId,
            comment.UserId,
            me ?? "Unknown",
            comment.Text,
            comment.CreatedAtUtc,
            new Dictionary<string, int>(),
            new List<string>()
        ));
    }

    [HttpDelete("posts/{postId}/comments/{commentId}")]
    public async Task<ActionResult> DeleteOwnComment(
        [FromRoute] string postId,
        [FromRoute] string commentId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (string.IsNullOrWhiteSpace(commentId)) return BadRequest("commentId is required.");

        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);
        if (error is not null) return error;

        var comment = await _db.HubPostComments
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId, ct);
        if (comment is null) return NotFound();

        var postBelongsToActiveEvent = await _db.HubPosts
            .AsNoTracking()
            .AnyAsync(x => x.Id == postId && x.EventId == access.EventId, ct);
        if (!postBelongsToActiveEvent) return NotFound();

        if (comment.UserId != access.UserId && !access.CanManage)
        {
            return Forbid();
        }

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

        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);
        if (error is not null) return error;

        var comment = await _db.HubPostComments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId, ct);
        if (comment is null) return NotFound();

        var postExists = await _db.HubPosts.AsNoTracking().AnyAsync(x => x.Id == postId && x.EventId == access.EventId, ct);
        if (!postExists) return NotFound();

        var emoji = string.IsNullOrWhiteSpace(req.Emoji) ? "â¤ï¸" : req.Emoji.Trim();
        if (emoji.Length > 16) emoji = emoji.Substring(0, 16);

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
    [RequestSizeLimit(25_000_000)] // 25MB
    public async Task<ActionResult<List<string>>> Upload([FromForm] IFormFileCollection files, CancellationToken ct = default)
    {
        if (files is null || files.Count == 0) return BadRequest("No files uploaded.");

        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);
        if (error is not null) return error;

        // Save *under* WebRootPath so the files are immediately available via UseStaticFiles().
        // If WebRootPath is ever null (non-standard host), fall back to <ContentRoot>/wwwroot.
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var hubDir = Path.Combine(webRoot, "uploads", "hub");
        Directory.CreateDirectory(hubDir);

        var urls = new List<string>();
        var mediaRecords = new List<HubPostMediaEntity>();

        var allowed = AllowedImageExtensions;

        foreach (var f in files.Take(10))
        {
            if (f.Length <= 0) continue;

            var ext = Path.GetExtension(f.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            if (!allowed.Contains(ext)) continue;

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var abs = Path.Combine(hubDir, fileName);

            await using var input = f.OpenReadStream();
            await using var ms = new MemoryStream();
            await input.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            await System.IO.File.WriteAllBytesAsync(abs, bytes, ct);

            var url = $"/uploads/hub/{fileName}";
            urls.Add(url);

            mediaRecords.Add(new HubPostMediaEntity
            {
                Url = url,
                OriginalFileName = f.FileName,
                FileSizeBytes = f.Length,
                UploadedByUserId = access.UserId == Guid.Empty ? null : access.UserId,
                ContentType = string.IsNullOrWhiteSpace(f.ContentType) ? null : f.ContentType,
                ContentBytes = bytes,
                UploadedAtUtc = DateTime.UtcNow
            });
        }

        if (mediaRecords.Count > 0)
        {
            _db.HubPostMedia.AddRange(mediaRecords);
            await _db.SaveChangesAsync(ct);
        }

        return Ok(urls);
    }

    [HttpPost("posts/{postId}/poll/vote")]
    public async Task<ActionResult<object>> VotePoll([FromRoute] string postId, [FromBody] VotePollRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");
        if (req is null || string.IsNullOrWhiteSpace(req.OptionId)) return BadRequest("optionId is required.");

        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);
        if (error is not null) return error;

        var pollExists = await _db.HubPostPolls.AnyAsync(x => x.PostId == postId, ct)
            && await _db.HubPosts.AnyAsync(x => x.Id == postId && x.EventId == access.EventId, ct);
        if (!pollExists) return NotFound();

        var optionExists = await _db.HubPostPollOptions.AnyAsync(x => x.Id == req.OptionId && x.PostId == postId, ct);
        if (!optionExists) return BadRequest("Invalid optionId.");

        var existing = await _db.HubPostPollVotes.SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == access.UserId, ct);

        if (existing is null)
        {
            _db.HubPostPollVotes.Add(new HubPostPollVoteEntity
            {
                PostId = postId,
                UserId = access.UserId,
                OptionId = req.OptionId.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            existing.OptionId = req.OptionId.Trim();
            existing.CreatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { optionId = req.OptionId.Trim() });
    }


    
    [HttpPost("posts/{postId}/like")]
    public async Task<ActionResult<object>> ToggleLike([FromRoute] string postId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);
        if (error is not null) return error;

        var postExists = await _db.HubPosts.AnyAsync(x => x.Id == postId && x.EventId == access.EventId, ct);
        if (!postExists) return NotFound();

        const string emoji = "â¤ï¸";
        var existing = await _db.HubPostReactions
            .SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == access.UserId && x.Emoji == emoji, ct);

        var liked = existing is null;
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
        return Ok(new { liked });
    }

    [HttpPost("posts/{postId}/reactions")]
    public async Task<ActionResult<object>> ToggleReaction([FromRoute] string postId, [FromBody] ToggleReactionRequest req, CancellationToken ct = default)
    {
        var emoji = string.IsNullOrWhiteSpace(req.Emoji) ? "â¤ï¸" : req.Emoji.Trim();
        if (emoji.Length > 16) emoji = emoji.Substring(0, 16);
        return await ToggleReactionInternal(postId, emoji, ct);
    }

    private async Task<ActionResult<object>> ToggleReactionInternal(string postId, string emoji, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postId)) return BadRequest("postId is required.");

        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);
        if (error is not null) return error;

        var postExists = await _db.HubPosts.AnyAsync(x => x.Id == postId && x.EventId == access.EventId, ct);
        if (!postExists) return NotFound();

        var existing = await _db.HubPostReactions
            .SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == access.UserId && x.Emoji == emoji, ct);

        var now = DateTime.UtcNow;

        var active = existing is null;
        if (existing is null)
        {
            _db.HubPostReactions.Add(new HubPostReactionEntity
            {
                PostId = postId,
                UserId = access.UserId,
                Emoji = emoji,
                CreatedAtUtc = now
            });
        }
        else
        {
            _db.HubPostReactions.Remove(existing);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { emoji, active });
    }


    // ------------------------------
    // Admin moderation
    // ------------------------------

    [HttpPost("admin/posts/{postId}/pin")]
    public async Task<ActionResult> SetPinned([FromRoute] string postId, [FromQuery] bool pinned = true, CancellationToken ct = default)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, requireManage: true);
        if (error is not null) return error;

        var post = await _db.HubPosts.SingleOrDefaultAsync(x => x.Id == postId && x.EventId == access.EventId, ct);
        if (post is null) return NotFound();

        post.IsPinned = pinned;
        await _db.SaveChangesAsync(ct);
        return Ok(new { pinned = post.IsPinned });
    }

    [HttpDelete("admin/posts/{postId}")]
    public async Task<ActionResult> DeletePost([FromRoute] string postId, CancellationToken ct = default)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, requireManage: true);
        if (error is not null) return error;

        var post = await _db.HubPosts.SingleOrDefaultAsync(x => x.Id == postId && x.EventId == access.EventId, ct);
        if (post is null) return NotFound();

        // delete children explicitly (no FKs declared)
        var likes = _db.HubPostLikes.Where(x => x.PostId == postId);
        var comments = _db.HubPostComments.Where(x => x.PostId == postId);
        var commentIds = _db.HubPostComments.Where(x => x.PostId == postId).Select(x => x.Id);
        var reactions = _db.HubPostReactions.Where(x => x.PostId == postId);
        var commentReactions = _db.HubPostCommentReactions.Where(x => commentIds.Contains(x.CommentId));

        _db.HubPostLikes.RemoveRange(likes);
        _db.HubPostCommentReactions.RemoveRange(commentReactions);
        _db.HubPostComments.RemoveRange(comments);
        _db.HubPostReactions.RemoveRange(reactions);
        _db.HubPosts.Remove(post);

        await _db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpDelete("admin/comments/{commentId}")]
    public async Task<ActionResult> DeleteComment([FromRoute] string commentId, CancellationToken ct = default)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, requireManage: true);
        if (error is not null) return error;

        var c = await _db.HubPostComments.SingleOrDefaultAsync(x => x.Id == commentId, ct);
        if (c is null) return NotFound();

        var postBelongsToActiveEvent = await _db.HubPosts.AsNoTracking()
            .AnyAsync(x => x.Id == c.PostId && x.EventId == access.EventId, ct);
        if (!postBelongsToActiveEvent) return NotFound();

        var commentReactions = _db.HubPostCommentReactions.Where(x => x.CommentId == commentId);
        _db.HubPostCommentReactions.RemoveRange(commentReactions);
        _db.HubPostComments.Remove(c);
        await _db.SaveChangesAsync(ct);
        return Ok();
    }

    private async Task<(ActiveFeedAccessContext Access, ActionResult? Error)> RequireActiveEventAccessAsync(
        CancellationToken ct,
        bool requireManage = false,
        EventModuleKey? moduleKey = null)
    {
        var activeEventId = await ResolveActiveEventIdAsync(ct);
        if (activeEventId is null) return (default!, NotFound());

        var userId = HttpContext.GetUserId();
        var isAdmin = HttpContext.IsAdmin();
        var isMember = await _db.EventMembers
            .AsNoTracking()
            .AnyAsync(x => x.EventId == activeEventId && x.UserId == userId, ct);

        var access = new ActiveFeedAccessContext(
            activeEventId,
            userId,
            isAdmin,
            isMember,
            await EventModuleAccessEvaluator.EvaluateAsync(_db, activeEventId, userId, isAdmin, ct));

        if (requireManage ? !access.CanManage : !access.CanAccess)
        {
            return (default!, Forbid());
        }

        if (moduleKey.HasValue && !EventModuleAccessEvaluator.IsModuleEnabled(access.ModuleAccess.EffectiveModules, moduleKey.Value))
        {
            return (default!, Forbid());
        }

        return (access, null);
    }

    private Task<string?> ResolveActiveEventIdAsync(CancellationToken ct) =>
        _db.Events
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);
}

