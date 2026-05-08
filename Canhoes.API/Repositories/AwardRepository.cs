using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Repositories;

public sealed class AwardRepository : IAwardRepository
{
    private readonly CanhoesDbContext _db;

    public AwardRepository(CanhoesDbContext db)
    {
        _db = db;
    }

    public Task<AwardCategoryEntity?> GetCategoryAsync(string categoryId, string eventId, CancellationToken ct) =>
        _db.AwardCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == categoryId && x.EventId == eventId, ct);

    public Task<List<AwardCategoryEntity>> GetActiveCategoriesAsync(string eventId, CancellationToken ct) =>
        _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

    public Task<int?> GetMaxSortOrderAsync(string eventId, CancellationToken ct) =>
        _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .MaxAsync(x => (int?)x.SortOrder, ct);

    public async Task AddCategoryAsync(AwardCategoryEntity category, CancellationToken ct) =>
        await _db.AwardCategories.AddAsync(category, ct);

    public Task<bool> CategoryExistsAsync(string eventId, string name, CancellationToken ct) =>
        _db.AwardCategories
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId && x.Name == name, ct);

    public Task<NomineeEntity?> GetNomineeAsync(string nomineeId, string eventId, CancellationToken ct) =>
        _db.Nominees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == nomineeId && x.EventId == eventId, ct);

    public Task<List<NomineeEntity>> GetNomineesAsync(string eventId, string? status, string? categoryId, CancellationToken ct)
    {
        var query = _db.Nominees.AsNoTracking().Where(x => x.EventId == eventId);
        if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(categoryId)) query = query.Where(x => x.CategoryId == categoryId);
        return query.ToListAsync(ct);
    }

    public async Task AddNomineeAsync(NomineeEntity nominee, CancellationToken ct) =>
        await _db.Nominees.AddAsync(nominee, ct);

    public async Task<int> CountSubmittedVotesAsync(Guid userId, IReadOnlyCollection<string> categoryIds, CancellationToken ct)
    {
        if (categoryIds.Count == 0) return 0;

        return await _db.Votes
                   .AsNoTracking()
                   .CountAsync(x => x.UserId == userId && categoryIds.Contains(x.CategoryId), ct)
               + await _db.UserVotes
                   .AsNoTracking()
                   .CountAsync(x => x.VoterUserId == userId && categoryIds.Contains(x.CategoryId), ct);
    }

    public async Task AddVoteAsync(VoteEntity vote, CancellationToken ct) =>
        await _db.Votes.AddAsync(vote, ct);

    public async Task AddUserVoteAsync(UserVoteEntity userVote, CancellationToken ct) =>
        await _db.UserVotes.AddAsync(userVote, ct);

    public Task<CategoryProposalEntity?> GetCategoryProposalAsync(string proposalId, string eventId, CancellationToken ct) =>
        _db.CategoryProposals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == proposalId && x.EventId == eventId, ct);

    public Task<MeasureProposalEntity?> GetMeasureProposalAsync(string proposalId, string eventId, CancellationToken ct) =>
        _db.MeasureProposals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == proposalId && x.EventId == eventId, ct);

    public Task<int> CountPendingCategoryProposalsAsync(string eventId, CancellationToken ct) =>
        _db.CategoryProposals
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId && x.Status == ProposalStatus.Pending, ct);

    public Task<List<NomineeEntity>> GetApprovedNomineesAsync(string eventId, CancellationToken ct) =>
        _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Status == ProposalStatus.Approved)
            .ToListAsync(ct);

    public Task<Dictionary<string, string>> GetUserNomineeVotesAsync(string eventId, Guid userId, CancellationToken ct) =>
        _db.Votes
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.UserId == userId)
            .ToDictionaryAsync(x => x.CategoryId, x => x.NomineeId, ct);

    public Task<Dictionary<string, Guid>> GetUserUserVotesAsync(string eventId, Guid userId, CancellationToken ct) =>
        _db.UserVotes
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.VoterUserId == userId)
            .ToDictionaryAsync(x => x.CategoryId, x => x.TargetUserId, ct);

    public async Task UpsertUserVoteAsync(string eventId, string categoryId, Guid userId, Guid targetUserId, CancellationToken ct)
    {
        var existing = await _db.UserVotes.FirstOrDefaultAsync(x => x.EventId == eventId && x.CategoryId == categoryId && x.VoterUserId == userId, ct);
        if (existing != null)
        {
            existing.TargetUserId = targetUserId;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            await _db.UserVotes.AddAsync(new UserVoteEntity
            {
                EventId = eventId,
                CategoryId = categoryId,
                VoterUserId = userId,
                TargetUserId = targetUserId,
                UpdatedAtUtc = DateTime.UtcNow
            }, ct);
        }
    }

    public async Task UpsertNomineeVoteAsync(string eventId, string categoryId, Guid userId, string nomineeId, CancellationToken ct)
    {
        var existing = await _db.Votes.FirstOrDefaultAsync(x => x.EventId == eventId && x.CategoryId == categoryId && x.UserId == userId, ct);
        if (existing != null)
        {
            existing.NomineeId = nomineeId;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            await _db.Votes.AddAsync(new VoteEntity
            {
                EventId = eventId,
                CategoryId = categoryId,
                UserId = userId,
                NomineeId = nomineeId,
                UpdatedAtUtc = DateTime.UtcNow
            }, ct);
        }
    }

    public async Task<(List<CategoryProposalEntity> Items, int Total)> GetProposalsPagedAsync(string eventId, int skip, int take, CancellationToken ct)
    {
        var query = _db.CategoryProposals.AsNoTracking().Where(x => x.EventId == eventId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public async Task AddCategoryProposalAsync(CategoryProposalEntity proposal, CancellationToken ct) =>
        await _db.CategoryProposals.AddAsync(proposal, ct);

    public Task<NomineeEntity?> GetLatestNomineeAsync(string eventId, Guid userId, CancellationToken ct) =>
        _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.SubmittedByUserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task<List<VoteEntity>> GetNomineeVotesAsync(string eventId, CancellationToken ct) =>
        _db.Votes
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .ToListAsync(ct);

    public Task<List<UserVoteEntity>> GetUserVotesAsync(string eventId, CancellationToken ct) =>
        _db.UserVotes
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .ToListAsync(ct);

    public async Task AddMeasureProposalAsync(MeasureProposalEntity proposal, CancellationToken ct) =>
        await _db.MeasureProposals.AddAsync(proposal, ct);

    public Task<List<GalaMeasureEntity>> GetMeasuresAsync(string eventId, CancellationToken ct) =>
        _db.Measures
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
