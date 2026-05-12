using Canhoes.Api.Models;

namespace Canhoes.Api.Repositories;

public interface IFeedRepository
{
    Task<HubPostEntity?> GetPostAsync(string postId, string eventId, CancellationToken ct);
    Task<List<HubPostEntity>> GetPostsPagedAsync(string eventId, int skip, int take, CancellationToken ct);
    Task<int> GetPostsCountAsync(string eventId, CancellationToken ct);
    
    Task<HubPostReactionEntity?> GetReactionAsync(string postId, Guid userId, string emoji, CancellationToken ct);
    Task AddReactionAsync(HubPostReactionEntity reaction, CancellationToken ct);
    Task DeleteReactionAsync(HubPostReactionEntity reaction, CancellationToken ct);
    
    Task<HubPostDownvoteEntity?> GetDownvoteAsync(string postId, Guid userId, CancellationToken ct);
    Task AddDownvoteAsync(HubPostDownvoteEntity downvote, CancellationToken ct);
    Task DeleteDownvoteAsync(HubPostDownvoteEntity downvote, CancellationToken ct);
    
    Task<List<HubPostCommentEntity>> GetCommentsAsync(string postId, string eventId, CancellationToken ct);
    Task<HubPostCommentEntity?> GetCommentAsync(string commentId, string postId, string eventId, CancellationToken ct);
    Task AddCommentAsync(HubPostCommentEntity comment, CancellationToken ct);
    Task DeleteCommentAsync(HubPostCommentEntity comment, CancellationToken ct);
    
    Task<List<HubPostCommentReactionEntity>> GetCommentReactionsAsync(List<string> commentIds, CancellationToken ct);
    Task<HubPostCommentReactionEntity?> GetCommentReactionAsync(string commentId, Guid userId, string emoji, CancellationToken ct);
    Task AddCommentReactionAsync(HubPostCommentReactionEntity reaction, CancellationToken ct);
    Task DeleteCommentReactionAsync(HubPostCommentReactionEntity reaction, CancellationToken ct);

    Task DeletePostAsync(HubPostEntity post, CancellationToken ct);

    // Polls
    Task<bool> PollExistsAsync(string postId, CancellationToken ct);
    Task<bool> PollOptionExistsAsync(string optionId, string postId, CancellationToken ct);
    Task<HubPostPollVoteEntity?> GetPollVoteAsync(string postId, Guid userId, CancellationToken ct);
    Task AddPollVoteAsync(HubPostPollVoteEntity vote, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
