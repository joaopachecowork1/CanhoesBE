using Canhoes.Api.Models;

namespace Canhoes.Api.Repositories;

public interface ISecretSantaRepository
{
    Task<SecretSantaDrawEntity?> GetLatestDrawAsync(string eventCode, CancellationToken ct);
    Task<SecretSantaAssignmentEntity?> GetAssignmentAsync(string drawId, Guid giverUserId, CancellationToken ct);
    Task<List<SecretSantaDrawEntity>> GetDrawsByEventCodeAsync(string eventCode, CancellationToken ct);
    Task AddDrawAsync(SecretSantaDrawEntity draw, CancellationToken ct);
    Task AddAssignmentAsync(SecretSantaAssignmentEntity assignment, CancellationToken ct);
    void RemoveDraws(IEnumerable<SecretSantaDrawEntity> draws);
    void RemoveAssignments(IEnumerable<SecretSantaAssignmentEntity> assignments);
    Task<List<SecretSantaAssignmentEntity>> GetAssignmentsByDrawIdAsync(string drawId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
