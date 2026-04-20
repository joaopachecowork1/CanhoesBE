using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.DTOs;
using Canhoes.Api.Media;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public sealed partial class HubController
{
    private sealed record HubFeedSnapshot(
        List<HubPostEntity> Posts,
        Dictionary<string, List<string>> MediaUrlsByPostId,
        Dictionary<string, int> CommentCounts,
        Dictionary<string, Dictionary<string, int>> ReactionCountsByPostId,
        Dictionary<string, List<string>> MyReactions,
        Dictionary<string, int> DownvoteCounts,
        Dictionary<string, bool> MyDownvotes,
        Dictionary<Guid, string> Authors,
        Dictionary<string, HubPollDto> PollsByPostId);

    private const string DefaultReactionEmoji = "\u2764\uFE0F";

    private Task<string?> ResolveActiveEventIdAsync(CancellationToken ct) =>
        _db.Events
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);

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

        if (moduleKey.HasValue &&
            !EventModuleAccessEvaluator.IsModuleEnabled(access.ModuleAccess.EffectiveModules, moduleKey.Value))
        {
            return (default!, Forbid());
        }

        return (access, null);
    }

    private Task<(ActiveFeedAccessContext Access, ActionResult? Error)> RequireFeedAccessAsync(CancellationToken ct) =>
        RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Feed);

    private Task<(ActiveFeedAccessContext Access, ActionResult? Error)> RequireFeedManageAccessAsync(CancellationToken ct) =>
        RequireActiveEventAccessAsync(ct, requireManage: true);

    private static string NormalizeReactionEmoji(string? emoji)
    {
        var value = string.IsNullOrWhiteSpace(emoji) ? DefaultReactionEmoji : emoji.Trim();
        return value.Length > 16 ? value[..16] : value;
    }

    private Task<bool> PostExistsInActiveEventAsync(string eventId, string postId, CancellationToken ct) =>
        _db.HubPosts
            .AsNoTracking()
            .AnyAsync(x => x.Id == postId && x.EventId == eventId, ct);

    private Task<string?> GetDisplayNameAsync(Guid userId, CancellationToken ct) =>
        _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName!)
            .SingleOrDefaultAsync(ct);

    private async Task<Dictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        return await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.Email : x.DisplayName!, ct);
    }

    private async Task<HubFeedSnapshot> LoadFeedSnapshotAsync(
        string eventId,
        int take,
        Guid userId,
        CancellationToken ct)
    {
        var posts = await _db.HubPosts
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);

        var postIds = posts.Select(post => post.Id).ToList();
        var mediaRecords = await _db.HubPostMedia
            .AsNoTracking()
            .Where(x => x.PostId != null && postIds.Contains(x.PostId))
            .OrderBy(x => x.UploadedAtUtc)
            .ToListAsync(ct);

        var mediaUrlsByPostId = mediaRecords
            .GroupBy(media => media.PostId!)
            .ToDictionary(
                group => group.Key,
                group => group.Select(media => media.Url).ToList());

        var commentCounts = await _db.HubPostComments
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .GroupBy(x => x.PostId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var reactions = await _db.HubPostReactions
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .Select(x => new { x.PostId, x.UserId, x.Emoji })
            .ToListAsync(ct);

        var reactionCountsByPostId = reactions
            .GroupBy(reaction => reaction.PostId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(reaction => reaction.Emoji)
                    .ToDictionary(emojiGroup => emojiGroup.Key, emojiGroup => emojiGroup.Count()));

        var myReactions = userId == Guid.Empty
            ? new Dictionary<string, List<string>>()
            : reactions
                .Where(reaction => reaction.UserId == userId)
                .GroupBy(reaction => reaction.PostId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(reaction => reaction.Emoji).Distinct().ToList());

        var downvotes = await _db.HubPostDownvotes
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .Select(x => new { x.PostId, x.UserId })
            .ToListAsync(ct);

        var downvoteCounts = downvotes
            .GroupBy(dv => dv.PostId)
            .ToDictionary(
                group => group.Key,
                group => group.Count());

        var myDownvotes = userId == Guid.Empty
            ? new Dictionary<string, bool>()
            : downvotes
                .Where(dv => dv.UserId == userId)
                .GroupBy(dv => dv.PostId)
                .ToDictionary(
                    group => group.Key,
                    group => true);

        return new HubFeedSnapshot(
            posts,
            mediaUrlsByPostId,
            commentCounts,
            reactionCountsByPostId,
            myReactions,
            downvoteCounts,
            myDownvotes,
            await GetDisplayNamesAsync(posts.Select(post => post.AuthorUserId), ct),
            await BuildPollsByPostIdAsync(postIds, userId, ct));
    }

    private async Task<Dictionary<string, HubPollDto>> BuildPollsByPostIdAsync(
        List<string> postIds,
        Guid userId,
        CancellationToken ct)
    {
        if (postIds.Count == 0) return new Dictionary<string, HubPollDto>();

        var polls = await _db.HubPostPolls
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .ToListAsync(ct);
        if (polls.Count == 0) return new Dictionary<string, HubPollDto>();

        var pollOptions = await _db.HubPostPollOptions
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        var pollVotes = await _db.HubPostPollVotes
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .Select(x => new { x.PostId, x.UserId, x.OptionId })
            .ToListAsync(ct);

        var optionsByPostId = pollOptions
            .GroupBy(option => option.PostId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var votesByPostId = pollVotes
            .GroupBy(vote => vote.PostId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return polls.ToDictionary(
            poll => poll.PostId,
            poll =>
            {
                var postVotes = votesByPostId.TryGetValue(poll.PostId, out var votes)
                    ? votes
                    : [];
                var voteCounts = postVotes
                    .GroupBy(vote => vote.OptionId)
                    .ToDictionary(group => group.Key, group => group.Count());
                var myOptionId = userId == Guid.Empty
                    ? null
                    : postVotes.FirstOrDefault(vote => vote.UserId == userId)?.OptionId;
                var options = optionsByPostId.TryGetValue(poll.PostId, out var options)
                    ? options.Select(option => new HubPollOptionDto
                    {
                        Id = option.Id,
                        Text = option.Text,
                        VoteCount = voteCounts.TryGetValue(option.Id, out var count) ? count : 0
                    }).ToList()
                    : [];

                return new HubPollDto
                {
                    Question = poll.Question,
                    Options = options,
                    MyOptionId = myOptionId,
                    TotalVotes = options.Sum(option => option.VoteCount)
                };
            });
    }

    private List<HubPostDto> BuildPostDtos(HubFeedSnapshot snapshot) =>
        snapshot.Posts.Select(post =>
        {
            var relatedMedia = snapshot.MediaUrlsByPostId.TryGetValue(post.Id, out var mediaUrls)
                ? mediaUrls
                : new List<string>();
            var media = MediaUrlFormatter.Collect(post.MediaUrl, post.MediaUrlsJson, relatedMedia);
            var reactionCounts = snapshot.ReactionCountsByPostId.TryGetValue(post.Id, out var counts)
                ? counts
                : new Dictionary<string, int>();
            var myReactions = snapshot.MyReactions.TryGetValue(post.Id, out var mine)
                ? mine
                : new List<string>();
            var downvoteCount = snapshot.DownvoteCounts.TryGetValue(post.Id, out var dvCount) ? dvCount : 0;
            var downvotedByMe = snapshot.MyDownvotes.TryGetValue(post.Id, out var dv) && dv;

            return new HubPostDto
            {
                Id = post.Id,
                AuthorUserId = post.AuthorUserId.ToString(),
                AuthorName = snapshot.Authors.TryGetValue(post.AuthorUserId, out var authorName) ? authorName : "Unknown",
                Text = post.Text,
                MediaUrl = media.FirstOrDefault(),
                MediaUrls = media,
                IsPinned = post.IsPinned,
                CreatedAtUtc = post.CreatedAtUtc,
                LikeCount = reactionCounts.TryGetValue(DefaultReactionEmoji, out var likeCount) ? likeCount : 0,
                CommentCount = snapshot.CommentCounts.TryGetValue(post.Id, out var commentCount) ? commentCount : 0,
                DownvoteCount = downvoteCount,
                ReactionCounts = reactionCounts,
                MyReactions = myReactions,
                LikedByMe = myReactions.Contains(DefaultReactionEmoji),
                DownvotedByMe = downvotedByMe,
                Poll = snapshot.PollsByPostId.TryGetValue(post.Id, out var poll) ? poll : null
            };
        }).ToList();

    private async Task AttachOrphanMediaAsync(
        List<string> mediaUrls,
        string postId,
        CancellationToken ct)
    {
        if (mediaUrls.Count == 0) return;

        var orphanMedia = await _db.HubPostMedia
            .Where(media => mediaUrls.Contains(media.Url) && media.PostId == null)
            .ToListAsync(ct);

        foreach (var media in orphanMedia)
        {
            media.PostId = postId;
        }

        if (orphanMedia.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<HubPollDto?> CreatePollAsync(
        string postId,
        string? pollQuestion,
        List<string> pollOptions,
        CancellationToken ct)
    {
        if (pollQuestion is null) return null;

        _db.HubPostPolls.Add(new HubPostPollEntity
        {
            PostId = postId,
            Question = pollQuestion,
            CreatedAtUtc = DateTime.UtcNow
        });

        var optionEntities = pollOptions
            .Select((text, index) => new HubPostPollOptionEntity
            {
                PostId = postId,
                Text = text.Length > 256 ? text[..256] : text,
                SortOrder = index
            })
            .ToList();

        _db.HubPostPollOptions.AddRange(optionEntities);
        await _db.SaveChangesAsync(ct);

        return new HubPollDto
        {
            Question = pollQuestion,
            Options = optionEntities
                .Select(option => new HubPollOptionDto { Id = option.Id, Text = option.Text, VoteCount = 0 })
                .ToList(),
            MyOptionId = null,
            TotalVotes = 0
        };
    }

    private async Task<List<HubCommentDto>> LoadCommentDtosAsync(
        string postId,
        Guid userId,
        CancellationToken ct)
    {
        var comments = await _db.HubPostComments
            .AsNoTracking()
            .Where(x => x.PostId == postId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        if (comments.Count == 0) return new List<HubCommentDto>();

        var commentIds = comments.Select(comment => comment.Id).ToList();
        var reactions = await _db.HubPostCommentReactions
            .AsNoTracking()
            .Where(x => commentIds.Contains(x.CommentId))
            .Select(x => new { x.CommentId, x.UserId, x.Emoji })
            .ToListAsync(ct);

        var reactionsByComment = reactions
            .GroupBy(reaction => reaction.CommentId)
            .ToDictionary(
                group => group.Key,
                group => group.GroupBy(reaction => reaction.Emoji)
                    .ToDictionary(emojiGroup => emojiGroup.Key, emojiGroup => emojiGroup.Count()));

        var users = await GetDisplayNamesAsync(comments.Select(comment => comment.UserId), ct);
        var myReactionLookup = userId == Guid.Empty
            ? new Dictionary<string, List<string>>()
            : reactions
                .Where(reaction => reaction.UserId == userId)
                .GroupBy(reaction => reaction.CommentId)
                .ToDictionary(group => group.Key, group => group.Select(reaction => reaction.Emoji).Distinct().ToList());

        return comments.Select(comment => new HubCommentDto(
            comment.Id,
            comment.PostId,
            comment.UserId,
            users.TryGetValue(comment.UserId, out var userName) ? userName : "Unknown",
            comment.Text,
            comment.CreatedAtUtc,
            reactionsByComment.TryGetValue(comment.Id, out var counts) ? counts : new Dictionary<string, int>(),
            myReactionLookup.TryGetValue(comment.Id, out var myReactions) ? myReactions : new List<string>())).ToList();
    }

    private async Task<HubCommentDto> BuildCreatedCommentDtoAsync(
        HubPostCommentEntity comment,
        CancellationToken ct)
    {
        var displayName = await GetDisplayNameAsync(comment.UserId, ct);
        return new HubCommentDto(
            comment.Id,
            comment.PostId,
            comment.UserId,
            displayName ?? "Unknown",
            comment.Text,
            comment.CreatedAtUtc,
            new Dictionary<string, int>(),
            new List<string>());
    }

    private async Task<List<string>> SaveUploadedFilesAsync(
        IFormFileCollection files,
        Guid userId,
        CancellationToken ct)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var hubDir = Path.Combine(webRoot, "uploads", "hub");
        Directory.CreateDirectory(hubDir);

        var urls = new List<string>();
        var mediaRecords = new List<HubPostMediaEntity>();

        foreach (var file in files.Take(10))
        {
            if (file.Length <= 0) continue;

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            if (!AllowedImageExtensions.Contains(ext)) continue;

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var abs = Path.Combine(hubDir, fileName);

            // OPTIMIZATION: Stream directly to disk instead of loading into memory twice
            await using var input = file.OpenReadStream();
            await using var output = new FileStream(abs, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await input.CopyToAsync(output, ct);

            var url = $"/uploads/hub/{fileName}";
            urls.Add(url);
            mediaRecords.Add(new HubPostMediaEntity
            {
                Url = url,
                OriginalFileName = file.FileName,
                FileSizeBytes = file.Length,
                UploadedByUserId = userId == Guid.Empty ? null : userId,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType,
                UploadedAtUtc = DateTime.UtcNow
            });
        }

        if (mediaRecords.Count > 0)
        {
            _db.HubPostMedia.AddRange(mediaRecords);
            await _db.SaveChangesAsync(ct);
        }

        return urls;
    }
}
