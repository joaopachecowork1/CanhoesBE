using Canhoes.Api.Data;
using System.Text.Json;
using Canhoes.Api.Access;
using Canhoes.Api.DTOs;
using Canhoes.Api.Media;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Canhoes.Api.Services;
using Canhoes.Api.Auth;
using static Canhoes.Api.Mappers.EventMappers;

namespace Canhoes.Api.Controllers;

[ApiController]
public class VotingController : EventControllerBase
{
    private readonly IAwardService _awardService;
    private readonly IEventService _eventService;

    public VotingController(
        IAwardService awardService,
        IEventService eventService,
        CanhoesDbContext db, 
        IMemoryCache cache, 
        IHubContext<Canhoes.Api.Hubs.EventHub> hub) 
        : base(db, cache, hub) 
    {
        _awardService = awardService;
        _eventService = eventService;
    }

    /// Gets the voting overview for the current event cycle.
    /// </summary>
    [HttpGet("{eventId}/voting/overview")]
    public async Task<ActionResult<EventVotingOverviewDto>> GetVotingOverview([FromRoute] string eventId, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (accessError is not null) return accessError;

        var votingPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        var activeCategories = await LoadActiveCategoriesAsync(eventId, ct);
        var submittedVoteCount = await CountSubmittedVotesAsync(
            userAccess.UserId,
            activeCategories.Select(x => x.Id).ToList(),
            ct);

        return Ok(new EventVotingOverviewDto(
            eventId,
            votingPhase?.Id,
            IsPhaseOpen(votingPhase),
            votingPhase is null ? null : new DateTimeOffset(votingPhase.EndDateUtc, TimeSpan.Zero),
            activeCategories.Count,
            submittedVoteCount,
            Math.Max(0, activeCategories.Count - submittedVoteCount)
        ));
    }

    /// <summary>
    /// Retrieves the voting board, including all active categories and member's current selections.
    /// </summary>
    [HttpGet("{eventId}/voting")]
    public async Task<ActionResult<EventVotingBoardDto>> GetVotingBoard([FromRoute] string eventId, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (accessError is not null) return accessError;

        var board = await _awardService.GetVotingBoardAsync(eventId, userAccess.UserId, ct);
        if (board is null) return NotFound();

        return Ok(board);
    }

    /// <summary>
    /// Casts or updates a vote for an award category.
    /// </summary>
    [HttpPost("{eventId}/votes")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("strict")]
    public async Task<ActionResult<EventVoteDto>> CastVote([FromRoute] string eventId, [FromBody] CreateEventVoteRequest voteRequest, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (accessError is not null) return accessError;

        var vote = await _awardService.CastVoteAsync(eventId, userAccess.UserId, voteRequest, ct);
        if (vote is null) return BadRequest("Could not cast vote.");

        return Ok(vote);
    }

    private async Task<ActionResult<EventVoteDto>> CastUserVoteAsync(string eventId, Guid userId, AwardCategoryEntity category, CreateEventVoteRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.SelectionId, out var targetUserId))
            return BadRequest("Invalid option.");

        var isUserMemberOfEvent = await _db.EventMembers
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId && x.UserId == targetUserId, ct);

        if (!isUserMemberOfEvent) return BadRequest("Invalid option.");

        var existingUserVoteEntity = await _db.UserVotes
            .FirstOrDefaultAsync(
                x => x.CategoryId == category.Id && x.VoterUserId == userId,
                ct);

        if (existingUserVoteEntity is null)
        {
            existingUserVoteEntity = new UserVoteEntity
            {
                Id = Guid.NewGuid().ToString(),
                EventId = eventId,
                CategoryId = category.Id,
                VoterUserId = userId,
                TargetUserId = targetUserId,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.UserVotes.Add(existingUserVoteEntity);
        }
        else
        {
            existingUserVoteEntity.TargetUserId = targetUserId;
            existingUserVoteEntity.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await NotifyVoteCastAsync(eventId, category.Id, request.SelectionId, ct);

        return Ok(new EventVoteDto(
            existingUserVoteEntity.CategoryId,
            existingUserVoteEntity.TargetUserId.ToString()
        ));
    }

    private async Task<ActionResult<EventVoteDto>> CastNomineeVoteAsync(string eventId, Guid userId, AwardCategoryEntity category, CreateEventVoteRequest request, CancellationToken ct)
    {
        var approvedNomineeEntity = await _db.Nominees
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == request.SelectionId
                && x.EventId == eventId
                && x.CategoryId == category.Id
                && x.Status == ProposalStatus.Approved, ct);

        if (approvedNomineeEntity is null) return BadRequest("Invalid option.");

        var existingNomineeVoteEntity = await _db.Votes
            .FirstOrDefaultAsync(x => x.CategoryId == category.Id && x.UserId == userId, ct);

        if (existingNomineeVoteEntity is null)
        {
            existingNomineeVoteEntity = new VoteEntity
            {
                Id = Guid.NewGuid().ToString(),
                EventId = eventId,
                CategoryId = category.Id,
                NomineeId = approvedNomineeEntity.Id,
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.Votes.Add(existingNomineeVoteEntity);
        }
        else
        {
            existingNomineeVoteEntity.NomineeId = approvedNomineeEntity.Id;
            existingNomineeVoteEntity.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await NotifyVoteCastAsync(eventId, category.Id, request.SelectionId, ct);

        return Ok(new EventVoteDto(
            existingNomineeVoteEntity.CategoryId,
            existingNomineeVoteEntity.NomineeId
        ));
    }

    private async Task NotifyVoteCastAsync(string eventId, string categoryId, string optionId, CancellationToken ct)
    {
        await _hub.Clients.Group($"event_{eventId}")
            .SendAsync("VoteCast", new { categoryId, optionId }, ct);
    }

    /// <summary>
    /// Lists all category proposals for the event (paginated).
    /// </summary>
    [HttpGet("{eventId}/proposals")]
    public async Task<ActionResult<PagedResult<EventProposalDto>>> GetProposals(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (accessError is not null) return accessError;

        return Ok(await _awardService.GetProposalsAsync(eventId, skip, take, ct));
    }

    /// <summary>
    /// Submits a new category proposal.
    /// </summary>
    [HttpPost("{eventId}/proposals")]
    public async Task<ActionResult<EventProposalDto>> CreateProposal([FromRoute] string eventId, [FromBody] CreateEventProposalRequest request, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (accessError is not null) return accessError;
        
        return Ok(await _awardService.CreateProposalAsync(eventId, userAccess.UserId, request, ct));
    }

    /// <summary>
    /// Updates an existing category proposal (admin only).
    /// </summary>
    [HttpPatch("{eventId}/proposals/{proposalId}")]
    public async Task<ActionResult<EventProposalDto>> UpdateProposal([FromRoute] string eventId, [FromRoute] string proposalId, [FromBody] UpdateEventProposalRequest request, CancellationToken ct)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (accessError is not null) return accessError;

        var proposal = await _awardService.UpdateCategoryProposalAsync(eventId, proposalId, new UpdateAdminCategoryProposalRequest(null, null, request.Status), ct);
        if (proposal is null) return NotFound();

        return Ok(ToEventProposalDto(new CategoryProposalEntity { Id = proposal.Id, Name = proposal.Name, Description = proposal.Description, Status = proposal.Status, CreatedAtUtc = proposal.CreatedAtUtc.UtcDateTime }));
    }
}
