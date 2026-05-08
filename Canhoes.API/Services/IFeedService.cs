using Canhoes.Api.DTOs;
using Canhoes.Api.Models;

namespace Canhoes.Api.Services;

public interface IFeedService
{
    // Posts
    Task<object> ToggleLikeAsync(string eventId, string postId, Guid userId, CancellationToken ct);
    Task<object> ToggleDownvoteAsync(string eventId, string postId, Guid userId, CancellationToken ct);
    Task<object> ToggleReactionAsync(string eventId, string postId, Guid userId, string emoji, CancellationToken ct);
    Task<List<HubCommentDto>> GetCommentsAsync(string eventId, string postId, Guid userId, CancellationToken ct);
    Task<HubCommentDto?> CreateCommentAsync(string eventId, string postId, Guid userId, string text, CancellationToken ct);
    Task<bool> DeleteCommentAsync(string eventId, string postId, string commentId, Guid userId, bool isAdmin, CancellationToken ct);
    Task<object> ToggleCommentReactionAsync(string eventId, string postId, string commentId, Guid userId, string emoji, CancellationToken ct);
    Task<object> VotePollAsync(string eventId, string postId, Guid userId, string optionId, CancellationToken ct);
    Task<bool> TogglePinAsync(string eventId, string postId, bool isAdmin, CancellationToken ct);
    Task<bool> DeletePostAsync(string eventId, string postId, bool isAdmin, CancellationToken ct);
    Task<List<EventFeedPostFullDto>> GetRecentPostsAsync(string eventId, Guid userId, int take, CancellationToken ct);
}
