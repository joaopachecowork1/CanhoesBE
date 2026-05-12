using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Repositories;

public sealed class FeedRepository : IFeedRepository
{
    private readonly CanhoesDbContext _db;

    public FeedRepository(CanhoesDbContext db)
    {
        _db = db;
    }

    public async Task<HubPostEntity?> GetPostAsync(string postId, string eventId, CancellationToken ct) =>
        await _db.HubPosts.FirstOrDefaultAsync(x => x.Id == postId && x.EventId == eventId, ct);

    public async Task<List<HubPostEntity>> GetPostsPagedAsync(string eventId, int skip, int take, CancellationToken ct) =>
        await _db.HubPosts.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<int> GetPostsCountAsync(string eventId, CancellationToken ct) =>
        await _db.HubPosts.CountAsync(x => x.EventId == eventId, ct);

    public async Task<HubPostReactionEntity?> GetReactionAsync(string postId, Guid userId, string emoji, CancellationToken ct) =>
        await _db.HubPostReactions.FirstOrDefaultAsync(x => x.PostId == postId && x.UserId == userId && x.Emoji == emoji, ct);

    public async Task AddReactionAsync(HubPostReactionEntity reaction, CancellationToken ct) =>
        await _db.HubPostReactions.AddAsync(reaction, ct);

    public async Task DeleteReactionAsync(HubPostReactionEntity reaction, CancellationToken ct)
    {
        _db.HubPostReactions.Remove(reaction);
        await Task.CompletedTask;
    }

    public async Task<HubPostDownvoteEntity?> GetDownvoteAsync(string postId, Guid userId, CancellationToken ct) =>
        await _db.HubPostDownvotes.FirstOrDefaultAsync(x => x.PostId == postId && x.UserId == userId, ct);

    public async Task AddDownvoteAsync(HubPostDownvoteEntity downvote, CancellationToken ct) =>
        await _db.HubPostDownvotes.AddAsync(downvote, ct);

    public async Task DeleteDownvoteAsync(HubPostDownvoteEntity downvote, CancellationToken ct)
    {
        _db.HubPostDownvotes.Remove(downvote);
        await Task.CompletedTask;
    }

    public async Task<List<HubPostCommentEntity>> GetCommentsAsync(string postId, string eventId, CancellationToken ct) =>
        await _db.HubPostComments.AsNoTracking()
            .Include(x => x.Post)
            .Where(x => x.PostId == postId && x.Post.EventId == eventId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<HubPostCommentEntity?> GetCommentAsync(string commentId, string postId, string eventId, CancellationToken ct) =>
        await _db.HubPostComments
            .Include(x => x.Post)
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId && x.Post.EventId == eventId, ct);

    public async Task AddCommentAsync(HubPostCommentEntity comment, CancellationToken ct) =>
        await _db.HubPostComments.AddAsync(comment, ct);

    public async Task DeleteCommentAsync(HubPostCommentEntity comment, CancellationToken ct)
    {
        _db.HubPostComments.Remove(comment);
        await Task.CompletedTask;
    }

    public async Task<List<HubPostCommentReactionEntity>> GetCommentReactionsAsync(List<string> commentIds, CancellationToken ct) =>
        await _db.HubPostCommentReactions.AsNoTracking()
            .Where(x => commentIds.Contains(x.CommentId))
            .ToListAsync(ct);

    public async Task<HubPostCommentReactionEntity?> GetCommentReactionAsync(string commentId, Guid userId, string emoji, CancellationToken ct) =>
        await _db.HubPostCommentReactions.FirstOrDefaultAsync(x => x.CommentId == commentId && x.UserId == userId && x.Emoji == emoji, ct);

    public async Task AddCommentReactionAsync(HubPostCommentReactionEntity reaction, CancellationToken ct) =>
        await _db.HubPostCommentReactions.AddAsync(reaction, ct);

    public async Task DeleteCommentReactionAsync(HubPostCommentReactionEntity reaction, CancellationToken ct)
    {
        _db.HubPostCommentReactions.Remove(reaction);
        await Task.CompletedTask;
    }

    public async Task DeletePostAsync(HubPostEntity post, CancellationToken ct)
    {
        _db.HubPosts.Remove(post);
        await Task.CompletedTask;
    }

    public async Task<bool> PollExistsAsync(string postId, CancellationToken ct) =>
        await _db.HubPostPolls.AsNoTracking().AnyAsync(x => x.PostId == postId, ct);

    public async Task<bool> PollOptionExistsAsync(string optionId, string postId, CancellationToken ct) =>
        await _db.HubPostPollOptions.AsNoTracking().AnyAsync(x => x.Id == optionId && x.PostId == postId, ct);

    public async Task<HubPostPollVoteEntity?> GetPollVoteAsync(string postId, Guid userId, CancellationToken ct) =>
        await _db.HubPostPollVotes.SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == userId, ct);

    public async Task AddPollVoteAsync(HubPostPollVoteEntity vote, CancellationToken ct) =>
        await _db.HubPostPollVotes.AddAsync(vote, ct);

    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);
}
