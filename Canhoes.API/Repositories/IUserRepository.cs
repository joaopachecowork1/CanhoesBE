using Canhoes.Api.Models;

namespace Canhoes.Api.Repositories;

public interface IUserRepository
{
    Task<UserEntity?> GetUserAsync(Guid userId, CancellationToken ct);
    Task<List<UserEntity>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct);
    Task<UserEntity?> GetUserByExternalIdAsync(string externalId, CancellationToken ct);
    Task<UserEntity> ResolveUserAsync(string externalId, string email, string? displayName, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
