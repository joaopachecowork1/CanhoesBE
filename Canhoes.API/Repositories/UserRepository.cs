using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly CanhoesDbContext _db;

    public UserRepository(CanhoesDbContext db)
    {
        _db = db;
    }

    public async Task<UserEntity?> GetUserAsync(Guid userId, CancellationToken ct) => 
        await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);

    public async Task<List<UserEntity>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct) => 
        await _db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToListAsync(ct);

    public async Task<UserEntity?> GetUserByExternalIdAsync(string externalId, CancellationToken ct) => 
        await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.ExternalId == externalId, ct);

    public async Task<UserEntity> ResolveUserAsync(string externalId, string email, string? displayName, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId || u.Email == email, ct);
        if (user is not null) return user;

        user = new UserEntity
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Email = email,
            DisplayName = displayName ?? email,
            IsAdmin = !await _db.Users.AnyAsync(ct),
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task SaveChangesAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
}
