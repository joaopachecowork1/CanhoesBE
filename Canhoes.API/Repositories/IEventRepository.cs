using Canhoes.Api.Models;

namespace Canhoes.Api.Repositories;

public interface IEventRepository
{
    Task<EventEntity?> GetActiveEventAsync(CancellationToken ct);
    Task<EventEntity?> GetEventAsync(string eventId, CancellationToken ct);
    Task<List<EventEntity>> GetAllEventsAsync(CancellationToken ct);
    Task<List<EventMemberEntity>> GetEventMembersAsync(string eventId, CancellationToken ct);
    Task<bool> IsUserMemberAsync(string eventId, Guid userId, CancellationToken ct);
    Task<List<EventPhaseEntity>> GetEventPhasesAsync(string eventId, CancellationToken ct);
    Task<EventPhaseEntity?> GetActivePhaseAsync(string eventId, string phaseType, CancellationToken ct);
    Task<CanhoesEventStateEntity?> GetEventStateAsync(string eventId, CancellationToken ct);
    Task AddEventStateAsync(CanhoesEventStateEntity state, CancellationToken ct);
    Task<WishlistItemEntity?> GetWishlistItemAsync(string itemId, string eventId, CancellationToken ct);
    Task DeleteWishlistItemAsync(WishlistItemEntity item, CancellationToken ct);
    Task<int> CountWishlistItemsAsync(string eventId, Guid userId, CancellationToken ct);
    Task<int> CountMembersAsync(string eventId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
