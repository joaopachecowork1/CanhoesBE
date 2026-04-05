using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public sealed partial class HubController
{
    [HttpPost("admin/posts/{postId}/pin")]
    public async Task<ActionResult> SetPinned([FromRoute] string postId, [FromQuery] bool pinned = true, CancellationToken ct = default)
    {
        var (access, error) = await RequireFeedManageAccessAsync(ct);
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
        var (access, error) = await RequireFeedManageAccessAsync(ct);
        if (error is not null) return error;

        var post = await _db.HubPosts.SingleOrDefaultAsync(x => x.Id == postId && x.EventId == access.EventId, ct);
        if (post is null) return NotFound();

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
        var (access, error) = await RequireFeedManageAccessAsync(ct);
        if (error is not null) return error;

        var comment = await _db.HubPostComments.SingleOrDefaultAsync(x => x.Id == commentId, ct);
        if (comment is null) return NotFound();

        var postBelongsToActiveEvent = await PostExistsInActiveEventAsync(access.EventId, comment.PostId, ct);
        if (!postBelongsToActiveEvent) return NotFound();

        var commentReactions = _db.HubPostCommentReactions.Where(x => x.CommentId == commentId);
        _db.HubPostCommentReactions.RemoveRange(commentReactions);
        _db.HubPostComments.Remove(comment);

        await _db.SaveChangesAsync(ct);
        return Ok();
    }
}
