using Canhoes.Api.Models;

namespace Canhoes.Api.Repositories;

public interface IAwardRepository
{
    // Categories
    Task<AwardCategoryEntity?> GetCategoryAsync(string categoryId, string eventId, CancellationToken ct);
    Task<List<AwardCategoryEntity>> GetActiveCategoriesAsync(string eventId, CancellationToken ct);
    Task<int?> GetMaxSortOrderAsync(string eventId, CancellationToken ct);
    Task AddCategoryAsync(AwardCategoryEntity category, CancellationToken ct);
    Task<bool> CategoryExistsAsync(string eventId, string name, CancellationToken ct);

    // Nominees
    Task<NomineeEntity?> GetNomineeAsync(string nomineeId, string eventId, CancellationToken ct);
    Task<List<NomineeEntity>> GetNomineesAsync(string eventId, string? status, string? categoryId, CancellationToken ct);
    Task<List<NomineeEntity>> GetApprovedNomineesAsync(string eventId, CancellationToken ct);
    Task<NomineeEntity?> GetLatestNomineeAsync(string eventId, Guid userId, CancellationToken ct);
    Task AddNomineeAsync(NomineeEntity nominee, CancellationToken ct);

    // Votes
    Task<int> CountSubmittedVotesAsync(Guid userId, IReadOnlyCollection<string> categoryIds, CancellationToken ct);
    Task<Dictionary<string, string>> GetUserNomineeVotesAsync(string eventId, Guid userId, CancellationToken ct);
    Task<Dictionary<string, Guid>> GetUserUserVotesAsync(string eventId, Guid userId, CancellationToken ct);
    Task<List<VoteEntity>> GetNomineeVotesAsync(string eventId, CancellationToken ct);
    Task<List<UserVoteEntity>> GetUserVotesAsync(string eventId, CancellationToken ct);
    Task UpsertUserVoteAsync(string eventId, string categoryId, Guid userId, Guid targetUserId, CancellationToken ct);
    Task UpsertNomineeVoteAsync(string eventId, string categoryId, Guid userId, string nomineeId, CancellationToken ct);
    Task AddVoteAsync(VoteEntity vote, CancellationToken ct);
    Task AddUserVoteAsync(UserVoteEntity userVote, CancellationToken ct);

    // Proposals
    Task<CategoryProposalEntity?> GetCategoryProposalAsync(string proposalId, string eventId, CancellationToken ct);
    Task<MeasureProposalEntity?> GetMeasureProposalAsync(string proposalId, string eventId, CancellationToken ct);
    Task<int> CountPendingCategoryProposalsAsync(string eventId, CancellationToken ct);
    Task<(List<CategoryProposalEntity> Items, int Total)> GetProposalsPagedAsync(string eventId, int skip, int take, CancellationToken ct);
    Task AddCategoryProposalAsync(CategoryProposalEntity proposal, CancellationToken ct);
    Task AddMeasureProposalAsync(MeasureProposalEntity proposal, CancellationToken ct);

    // Measures
    Task<List<GalaMeasureEntity>> GetMeasuresAsync(string eventId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
