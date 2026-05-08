using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Repositories;

public sealed class EventRepository : IEventRepository
{
    private readonly CanhoesDbContext _db;

    public EventRepository(CanhoesDbContext db)
    {
        _db = db;
    }

    public Task<EventEntity?> GetActiveEventAsync(CancellationToken ct) =>
        _db.Events.AsNoTracking().OrderByDescending(x => x.IsActive).ThenByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);

    public Task<EventEntity?> GetEventAsync(string eventId, CancellationToken ct) =>
        _db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == eventId, ct);

    public Task<List<EventEntity>> GetAllEventsAsync(CancellationToken ct) =>
        _db.Events.AsNoTracking().OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).ToListAsync(ct);

    public Task<List<EventMemberEntity>> GetEventMembersAsync(string eventId, CancellationToken ct) =>
        _db.EventMembers.AsNoTracking().Where(x => x.EventId == eventId).ToListAsync(ct);

    public Task<bool> IsUserMemberAsync(string eventId, Guid userId, CancellationToken ct) =>
        _db.EventMembers.AsNoTracking().AnyAsync(x => x.EventId == eventId && x.UserId == userId, ct);

    public Task<List<EventPhaseEntity>> GetEventPhasesAsync(string eventId, CancellationToken ct) =>
        _db.EventPhases.AsNoTracking().Where(x => x.EventId == eventId).OrderBy(x => x.StartDateUtc).ToListAsync(ct);

    public Task<EventPhaseEntity?> GetActivePhaseAsync(string eventId, string phaseType, CancellationToken ct) =>
        _db.EventPhases
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Type == phaseType && x.IsActive)
            .OrderByDescending(x => x.StartDateUtc)
            .FirstOrDefaultAsync(ct);

    public Task<CanhoesEventStateEntity?> GetEventStateAsync(string eventId, CancellationToken ct) =>
        _db.CanhoesEventState.FirstOrDefaultAsync(x => x.EventId == eventId, ct);

    public async Task AddEventStateAsync(CanhoesEventStateEntity state, CancellationToken ct) =>
        await _db.CanhoesEventState.AddAsync(state, ct);

    public Task<int> CountMembersAsync(string eventId, CancellationToken ct) =>
        _db.EventMembers.AsNoTracking().CountAsync(x => x.EventId == eventId, ct);

    public Task<WishlistItemEntity?> GetWishlistItemAsync(string itemId, string eventId, CancellationToken ct) =>
        _db.WishlistItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == itemId && x.EventId == eventId, ct);

    public async Task DeleteWishlistItemAsync(WishlistItemEntity item, CancellationToken ct)
    {
        _db.WishlistItems.Remove(item);
        await Task.CompletedTask;
    }

    public Task<int> CountWishlistItemsAsync(string eventId, Guid userId, CancellationToken ct) =>
        _db.WishlistItems.AsNoTracking().CountAsync(x => x.EventId == eventId && x.UserId == userId, ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
