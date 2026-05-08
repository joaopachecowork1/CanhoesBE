using Canhoes.Api.DTOs;
using Canhoes.Api.Models;

namespace Canhoes.Api.Services;

public interface IAwardService
{
    // Categories
    Task<List<AwardCategoryDto>> GetAdminCategoriesAsync(string eventId, CancellationToken ct);
    Task<AwardCategoryDto> CreateCategoryAsync(string eventId, CreateAwardCategoryRequest request, CancellationToken ct);
    Task<AwardCategoryDto?> UpdateCategoryAsync(string eventId, string categoryId, UpdateAwardCategoryRequest request, CancellationToken ct);
    Task<bool> DeleteCategoryAsync(string eventId, string categoryId, CancellationToken ct);
    Task<PagedResult<AwardCategoryDto>> GetActiveCategoriesPagedAsync(string eventId, int skip, int take, CancellationToken ct);

    // Proposals
    Task<PagedResult<CategoryProposalDto>> GetCategoryProposalsAsync(string eventId, string? status, int skip, int take, CancellationToken ct);
    Task<CategoryProposalDto?> UpdateCategoryProposalAsync(string eventId, string proposalId, UpdateAdminCategoryProposalRequest request, CancellationToken ct);
    Task<bool> DeleteCategoryProposalAsync(string eventId, string proposalId, CancellationToken ct);

    // Nominations
    Task<AdminNomineesPagedDto> GetNominationsPagedAsync(string eventId, string? status, int skip, int take, CancellationToken ct);
    Task<AdminNomineeDto?> SetNominationCategoryAsync(string eventId, string nomineeId, string? categoryId, CancellationToken ct);
    Task<AdminNomineeDto?> ApproveNominationAsync(string eventId, string nomineeId, CancellationToken ct);
    Task<AdminNomineeDto?> RejectNominationAsync(string eventId, string nomineeId, CancellationToken ct);

    // Votes
    Task<AdminVotesPagedDto> GetVotesPagedAsync(string eventId, int skip, int take, CancellationToken ct);
    Task<EventVotingBoardDto> GetVotingBoardAsync(string eventId, Guid userId, CancellationToken ct);
    Task<EventVoteDto?> CastVoteAsync(string eventId, Guid userId, CreateEventVoteRequest request, CancellationToken ct);

    Task<PagedResult<EventProposalDto>> GetProposalsAsync(string eventId, int skip, int take, CancellationToken ct);
    Task<EventProposalDto> CreateProposalAsync(string eventId, Guid userId, CreateEventProposalRequest request, CancellationToken ct);
}
