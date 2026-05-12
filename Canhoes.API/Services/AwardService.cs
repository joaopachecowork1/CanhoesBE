using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using Canhoes.Api.Repositories;
using static Canhoes.Api.Mappers.EventMappers;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Services;

public sealed class AwardService : IAwardService
{
    private readonly IAwardRepository _awardRepository;
    private readonly IUserRepository _userRepository;
    private readonly IEventRepository _eventRepository;

    public AwardService(
        IAwardRepository awardRepository,
        IUserRepository userRepository,
        IEventRepository eventRepository)
    {
        _awardRepository = awardRepository;
        _userRepository = userRepository;
        _eventRepository = eventRepository;
    }

    public async Task<List<AwardCategoryDto>> GetAdminCategoriesAsync(string eventId, CancellationToken ct)
    {
        var categories = await _awardRepository.GetActiveCategoriesAsync(eventId, ct);
        return categories.Select(ToAwardCategoryDto).ToList();
    }

    public async Task<AwardCategoryDto> CreateCategoryAsync(string eventId, CreateAwardCategoryRequest request, CancellationToken ct)
    {
        var nextSortOrder = request.SortOrder
            ?? (await _awardRepository.GetMaxSortOrderAsync(eventId, ct) ?? 0) + 1;

        if (!Enum.TryParse<AwardCategoryKind>(request.Kind, true, out var kind))
        {
            kind = AwardCategoryKind.Sticker;
        }

        var category = new AwardCategoryEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Kind = kind,
            SortOrder = nextSortOrder,
            IsActive = true,
            VoteQuestion = string.IsNullOrWhiteSpace(request.VoteQuestion) ? null : request.VoteQuestion.Trim(),
            VoteRules = string.IsNullOrWhiteSpace(request.VoteRules) ? null : request.VoteRules.Trim()
        };

        await _awardRepository.AddCategoryAsync(category, ct);
        await _awardRepository.SaveChangesAsync(ct);

        return ToAwardCategoryDto(category);
    }

    public async Task<AwardCategoryDto?> UpdateCategoryAsync(string eventId, string categoryId, UpdateAwardCategoryRequest request, CancellationToken ct)
    {
        var category = await _awardRepository.GetCategoryAsync(categoryId, eventId, ct);
        if (category is null) return null;

        if (request.Name is not null) category.Name = request.Name.Trim();
        if (request.Description is not null) category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (request.SortOrder.HasValue) category.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue) category.IsActive = request.IsActive.Value;
        if (request.Kind is not null && Enum.TryParse<AwardCategoryKind>(request.Kind, true, out var kind)) category.Kind = kind;
        if (request.VoteQuestion is not null) category.VoteQuestion = string.IsNullOrWhiteSpace(request.VoteQuestion) ? null : request.VoteQuestion.Trim();
        if (request.VoteRules is not null) category.VoteRules = string.IsNullOrWhiteSpace(request.VoteRules) ? null : request.VoteRules.Trim();

        await _awardRepository.SaveChangesAsync(ct);
        return ToAwardCategoryDto(category);
    }

    public async Task<bool> DeleteCategoryAsync(string eventId, string categoryId, CancellationToken ct)
    {
        var category = await _awardRepository.GetCategoryAsync(categoryId, eventId, ct);
        if (category is null) return false;
        // Logic for deletion if needed (e.g. soft delete or check constraints)
        return true; 
    }

    public async Task<PagedResult<AwardCategoryDto>> GetActiveCategoriesPagedAsync(string eventId, int skip, int take, CancellationToken ct)
    {
        var (items, total) = await _awardRepository.GetActiveCategoriesPagedAsync(eventId, skip, take, ct);
        return new PagedResult<AwardCategoryDto>(
            items.Select(ToAwardCategoryDto).ToList(),
            total,
            skip,
            take,
            skip + take < total);
    }

    public async Task<PagedResult<CategoryProposalDto>> GetCategoryProposalsAsync(string eventId, string? status, int skip, int take, CancellationToken ct)
    {
        // Placeholder, implement properly with repo
        return PagedResult<CategoryProposalDto>.Empty(skip, take);
    }

    public async Task<CategoryProposalDto?> UpdateCategoryProposalAsync(string eventId, string proposalId, UpdateAdminCategoryProposalRequest request, CancellationToken ct)
    {
        var proposal = await _awardRepository.GetCategoryProposalAsync(proposalId, eventId, ct);
        if (proposal is null) return null;

        if (request.Name is not null) proposal.Name = request.Name.Trim();
        if (request.Description is not null) proposal.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        
        if (request.Status is not null)
        {
            proposal.Status = request.Status;
            if (request.Status == ProposalStatus.Approved)
            {
                var exists = await _awardRepository.CategoryExistsAsync(eventId, proposal.Name, ct);
                if (!exists)
                {
                    await CreateCategoryAsync(eventId, new CreateAwardCategoryRequest(proposal.Name, null, "Sticker", proposal.Description, null, null), ct);
                }
            }
        }

        await _awardRepository.SaveChangesAsync(ct);
        return ToCategoryProposalDto(proposal);
    }

    public async Task<bool> DeleteCategoryProposalAsync(string eventId, string proposalId, CancellationToken ct)
    {
        var proposal = await _awardRepository.GetCategoryProposalAsync(proposalId, eventId, ct);
        if (proposal is null) return false;
        return true;
    }

    public async Task<AdminNomineesPagedDto> GetNominationsPagedAsync(string eventId, string? status, int skip, int take, CancellationToken ct)
    {
        return new AdminNomineesPagedDto(0, [], skip, take, false);
    }

    public async Task<AdminNomineeDto?> SetNominationCategoryAsync(string eventId, string nomineeId, string? categoryId, CancellationToken ct)
    {
        var nominee = await _awardRepository.GetNomineeAsync(nomineeId, eventId, ct);
        if (nominee is null) return null;

        nominee.CategoryId = categoryId;
        await _awardRepository.SaveChangesAsync(ct);
        
        var user = await _userRepository.GetUserAsync(nominee.SubmittedByUserId, ct);
        return ToAdminNomineeDto(nominee, user);
    }

    public async Task<AdminNomineeDto?> ApproveNominationAsync(string eventId, string nomineeId, CancellationToken ct)
    {
        var nominee = await _awardRepository.GetNomineeAsync(nomineeId, eventId, ct);
        if (nominee is null) return null;

        nominee.Status = ProposalStatus.Approved;
        await _awardRepository.SaveChangesAsync(ct);

        var user = await _userRepository.GetUserAsync(nominee.SubmittedByUserId, ct);
        return ToAdminNomineeDto(nominee, user);
    }

    public async Task<AdminNomineeDto?> RejectNominationAsync(string eventId, string nomineeId, CancellationToken ct)
    {
        var nominee = await _awardRepository.GetNomineeAsync(nomineeId, eventId, ct);
        if (nominee is null) return null;

        nominee.Status = ProposalStatus.Rejected;
        await _awardRepository.SaveChangesAsync(ct);

        var user = await _userRepository.GetUserAsync(nominee.SubmittedByUserId, ct);
        return ToAdminNomineeDto(nominee, user);
    }

    public async Task<AdminVotesPagedDto> GetVotesPagedAsync(string eventId, int skip, int take, CancellationToken ct)
    {
        return new AdminVotesPagedDto(0, [], skip, take, false);
    }

    public async Task<EventVotingBoardDto> GetVotingBoardAsync(string eventId, Guid userId, CancellationToken ct)
    {
        var activeVotingPhase = await _eventRepository.GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        var categories = await _awardRepository.GetActiveCategoriesAsync(eventId, ct);
        var nominees = await _awardRepository.GetApprovedNomineesAsync(eventId, ct);
        var members = await _eventRepository.GetEventMembersAsync(eventId, ct);
        var users = await _userRepository.GetUsersAsync(members.Select(m => m.UserId).ToList(), ct);
        var usersById = users.ToDictionary(u => u.Id);

        var nomineeVotes = await _awardRepository.GetUserNomineeVotesAsync(eventId, userId, ct);
        var userVotes = await _awardRepository.GetUserUserVotesAsync(eventId, userId, ct);

        var categoriesDto = categories.Select(category =>
        {
            List<EventVoteOptionDto> options;
            string? mySelectionId = null;

            if (category.Kind == AwardCategoryKind.UserVote)
            {
                options = members
                    .Where(m => usersById.ContainsKey(m.UserId))
                    .OrderByDescending(m => m.Role == EventRoles.Admin)
                    .ThenBy(m => usersById[m.UserId].DisplayName ?? usersById[m.UserId].Email)
                    .Select(m => new EventVoteOptionDto(m.UserId.ToString(), category.Id, usersById[m.UserId].DisplayName ?? usersById[m.UserId].Email))
                    .ToList();

                if (userVotes.TryGetValue(category.Id, out var targetUserId))
                {
                    mySelectionId = targetUserId.ToString();
                }
            }
            else
            {
                options = nominees
                    .Where(n => n.CategoryId == category.Id)
                    .Select(n => new EventVoteOptionDto(n.Id, category.Id, n.Title))
                    .ToList();

                if (nomineeVotes.TryGetValue(category.Id, out var nomineeId))
                {
                    mySelectionId = nomineeId;
                }
            }

            return new EventVotingCategoryDto(
                category.Id,
                category.EventId,
                category.Name,
                category.Kind.ToString(),
                category.Description,
                category.VoteQuestion,
                options,
                mySelectionId
            );
        }).ToList();

        return new EventVotingBoardDto(
            eventId,
            activeVotingPhase?.Id,
            activeVotingPhase != null && activeVotingPhase.StartDateUtc <= DateTime.UtcNow && activeVotingPhase.EndDateUtc >= DateTime.UtcNow,
            categoriesDto
        );
    }

    public async Task<EventVoteDto?> CastVoteAsync(string eventId, Guid userId, CreateEventVoteRequest request, CancellationToken ct)
    {
        var category = await _awardRepository.GetCategoryAsync(request.CategoryId, eventId, ct);
        if (category is null || !category.IsActive) return null;

        if (category.Kind == AwardCategoryKind.UserVote)
        {
            if (!Guid.TryParse(request.SelectionId, out var targetUserId)) return null;
            await _awardRepository.UpsertUserVoteAsync(eventId, category.Id, userId, targetUserId, ct);
        }
        else
        {
            await _awardRepository.UpsertNomineeVoteAsync(eventId, category.Id, userId, request.SelectionId, ct);
        }

        await _awardRepository.SaveChangesAsync(ct);
        return new EventVoteDto(request.CategoryId, request.SelectionId);
    }

    public async Task<PagedResult<EventProposalDto>> GetProposalsAsync(string eventId, int skip, int take, CancellationToken ct)
    {
        var (proposals, total) = await _awardRepository.GetProposalsPagedAsync(eventId, skip, take, ct);
        return new PagedResult<EventProposalDto>(
            proposals.Select(ToEventProposalDto).ToList(),
            total,
            skip,
            take,
            (skip + take) < total
        );
    }

    public async Task<EventProposalDto> CreateProposalAsync(string eventId, Guid userId, CreateEventProposalRequest request, CancellationToken ct)
    {
        var proposal = new CategoryProposalEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            ProposedByUserId = userId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Status = ProposalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _awardRepository.AddCategoryProposalAsync(proposal, ct);
        await _awardRepository.SaveChangesAsync(ct);

        return ToEventProposalDto(proposal);
    }
}
