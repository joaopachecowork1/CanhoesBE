using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Repositories;

public sealed class SecretSantaRepository : ISecretSantaRepository
{
    private readonly CanhoesDbContext _db;

    public SecretSantaRepository(CanhoesDbContext db)
    {
        _db = db;
    }

    public Task<SecretSantaDrawEntity?> GetLatestDrawAsync(string eventCode, CancellationToken ct) =>
        _db.SecretSantaDraws
            .AsNoTracking()
            .Where(x => x.EventCode == eventCode)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task<SecretSantaAssignmentEntity?> GetAssignmentAsync(string drawId, Guid giverUserId, CancellationToken ct) =>
        _db.SecretSantaAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DrawId == drawId && x.GiverUserId == giverUserId, ct);

    public Task<List<SecretSantaDrawEntity>> GetDrawsByEventCodeAsync(string eventCode, CancellationToken ct) =>
        _db.SecretSantaDraws
            .Where(x => x.EventCode == eventCode)
            .ToListAsync(ct);

    public async Task AddDrawAsync(SecretSantaDrawEntity draw, CancellationToken ct) =>
        await _db.SecretSantaDraws.AddAsync(draw, ct);

    public async Task AddAssignmentAsync(SecretSantaAssignmentEntity assignment, CancellationToken ct) =>
        await _db.SecretSantaAssignments.AddAsync(assignment, ct);

    public void RemoveDraws(IEnumerable<SecretSantaDrawEntity> draws) =>
        _db.SecretSantaDraws.RemoveRange(draws);

    public void RemoveAssignments(IEnumerable<SecretSantaAssignmentEntity> assignments) =>
        _db.SecretSantaAssignments.RemoveRange(assignments);

    public Task<List<SecretSantaAssignmentEntity>> GetAssignmentsByDrawIdAsync(string drawId, CancellationToken ct) =>
        _db.SecretSantaAssignments
            .Where(x => x.DrawId == drawId)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
