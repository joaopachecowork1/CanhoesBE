using Microsoft.Extensions.Caching.Memory;
using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Models;
using Canhoes.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Canhoes.Api.Services;
using Canhoes.Api.Mappers;
using Canhoes.Api.Data;
using static Canhoes.Api.Mappers.EventMappers;

namespace Canhoes.Api.Controllers;

[Authorize(Policy = "AdminOnly")]
public sealed class AdminEventsController : EventControllerBase
{
    private readonly IEventService _eventService;
    private readonly IAwardService _awardService;
    private readonly ISecretSantaService _secretSanta;

    public AdminEventsController(
        IEventService eventService,
        IAwardService awardService,
        ISecretSantaService secretSanta,
        CanhoesDbContext db, 
        IMemoryCache? cache = null, 
        Microsoft.AspNetCore.SignalR.IHubContext<Canhoes.Api.Hubs.EventHub>? hub = null, 
        IWebHostEnvironment? env = null) 
        : base(db, cache, hub, env)
    {
        _eventService = eventService;
        _awardService = awardService;
        _secretSanta = secretSanta;
    }
    /// <summary>
    /// Lists all award categories for the specified event.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of award categories.</returns>
    [HttpGet("{eventId}/admin/categories")]
    public async Task<ActionResult<List<AwardCategoryDto>>> AdminGetCategories([FromRoute] string eventId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;
        return Ok(await _awardService.GetAdminCategoriesAsync(eventId, ct));
    }

    /// <summary>
    /// Creates a new award category for the event.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="request">The category creation details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created award category.</returns>
    [HttpPost("{eventId}/admin/categories")]
    public async Task<ActionResult<AwardCategoryDto>> AdminCreateCategory([FromRoute] string eventId, [FromBody] CreateAwardCategoryRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");
        
        var category = await _awardService.CreateCategoryAsync(eventId, request, ct);
        return Ok(category);
    }

    /// <summary>
    /// Updates an existing award category.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="request">The update details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated award category.</returns>
    [HttpPut("{eventId}/admin/categories/{categoryId}")]
    public async Task<ActionResult<AwardCategoryDto>> AdminUpdateCategory([FromRoute] string eventId, [FromRoute] string categoryId, [FromBody] UpdateAwardCategoryRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var category = await _awardService.UpdateCategoryAsync(eventId, categoryId, request, ct);
        if (category is null) return NotFound();

        return Ok(category);
    }

    /// <summary>
    /// Deletes an award category if it has no dependent nominees or votes.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{eventId}/admin/categories/{categoryId}")]
    public async Task<ActionResult> AdminDeleteCategory([FromRoute] string eventId, [FromRoute] string categoryId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var awardCategory = await FindAwardCategoryAsync(eventId, categoryId, ct);
        if (awardCategory is null) return NotFound();

        var hasDependents =
            await _db.Nominees.AsNoTracking().AnyAsync(
                x => x.EventId == eventId && x.CategoryId == categoryId,
                ct)
            || await _db.Votes.AsNoTracking().AnyAsync(x => x.CategoryId == categoryId, ct)
            || await _db.UserVotes.AsNoTracking().AnyAsync(x => x.CategoryId == categoryId, ct);

        if (hasDependents)
        {
            return BadRequest(
                "Category has dependent nominees or votes and cannot be deleted.");
        }

        _db.AwardCategories.Remove(awardCategory);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Gets the current administrative state for the event, including module visibility and phase.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The administrative state of the event.</returns>
    [HttpGet("{eventId}/admin/state")]
    public async Task<ActionResult<EventAdminStateDto>> GetAdminState([FromRoute] string eventId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        return Ok(await BuildAdminStateDtoAsync(eventId, ct));
    }

    /// <summary>
    /// Bootstraps the admin panel with events, current state, and aggregate counts.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="includeLists">Legacy parameter, no longer impacts response size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The admin bootstrap payload.</returns>
    [HttpGet("{eventId}/admin/bootstrap")]
    public async Task<ActionResult<EventAdminBootstrapDto>> GetAdminBootstrap(
        [FromRoute] string eventId,
        [FromQuery] bool includeLists = false,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        return Ok(await BuildAdminBootstrapDtoAsync(eventId, includeLists, ct));
    }

    /// <summary>
    /// Updates administrative state flags such as nomination and result visibility.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="request">The state update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated administrative state.</returns>
    [HttpPut("{eventId}/admin/state")]
    public async Task<ActionResult<EventAdminStateDto>> UpdateAdminState([FromRoute] string eventId, [FromBody] UpdateEventAdminStateRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var legacyState = await GetOrCreateEventStateAsync(eventId, ct);
        var nextModuleVisibility = request.ModuleVisibility ?? EventModuleAccessEvaluator.ParseModuleVisibility(legacyState);

        if (request.NominationsVisible.HasValue)
        {
            legacyState.NominationsVisible = request.NominationsVisible.Value;
        }

        if (request.ResultsVisible.HasValue)
        {
            legacyState.ResultsVisible = request.ResultsVisible.Value;
        }

        legacyState.ModuleVisibilityJson = EventModuleAccessEvaluator.SerializeModuleVisibility(nextModuleVisibility);
        await _db.SaveChangesAsync(ct);

        return Ok(await BuildAdminStateDtoAsync(eventId, ct));
    }

    /// <summary>
    /// Updates the module visibility for members in a specific event.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="request">The module update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated event overview.</returns>
    [HttpPatch("{eventId}/modules")]
    public async Task<ActionResult<EventOverviewDto>> UpdateEventModules([FromRoute] string eventId, [FromBody] UpdateEventModulesRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var legacyState = await GetOrCreateEventStateAsync(eventId, ct);
        legacyState.ModuleVisibilityJson = EventModuleAccessEvaluator.SerializeModuleVisibility(
            new EventAdminModuleVisibilityDto(
                request.Modules.Feed,
                request.Modules.SecretSanta,
                request.Modules.Wishlist,
                request.Modules.Categories,
                request.Modules.Voting,
                request.Modules.Gala,
                request.Modules.Stickers,
                request.Modules.Measures,
                request.Modules.Nominees
            ));

        await _db.SaveChangesAsync(ct);
        var overview = await _eventService.GetEventOverviewAsync(eventId, HttpContext.GetUserId(), true, ct);
        if (overview is null) return NotFound();
        return Ok(overview);
    }

    /// <summary>
    /// Switches the active phase of the event.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="request">The phase update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated administrative state.</returns>
    [HttpPut("{eventId}/admin/phase")]
    public async Task<ActionResult<EventAdminStateDto>> UpdateAdminPhase([FromRoute] string eventId, [FromBody] UpdateEventPhaseRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;
        if (string.IsNullOrWhiteSpace(request.PhaseType)) return BadRequest("PhaseType is required.");

        var phases = await LoadEventPhasesForUpdateAsync(eventId, ct);

        var targetPhase = phases.FirstOrDefault(x =>
            x.Type.Equals(request.PhaseType.Trim(), StringComparison.OrdinalIgnoreCase));

        if (targetPhase is null)
        {
            return BadRequest("Invalid phase.");
        }

        foreach (var phase in phases)
        {
            phase.IsActive = phase.Id == targetPhase.Id;
        }

        var legacyState = await GetOrCreateEventStateAsync(eventId, ct);
        EventModuleAccessEvaluator.ApplyLegacyStateForPhase(legacyState, targetPhase.Type);

        await _db.SaveChangesAsync(ct);
        return Ok(await BuildAdminStateDtoAsync(eventId, ct));
    }

    /// <summary>
    /// Activates an event, making it the primary event and deactivating others.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A summary of the activated event.</returns>
    [HttpPut("{eventId}/admin/activate")]
    public async Task<ActionResult<EventSummaryDto>> ActivateEvent([FromRoute] string eventId, CancellationToken ct)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (accessError is not null) return accessError;

        var eventEntities = await _db.Events
            .Select(x => new EventEntity { Id = x.Id, IsActive = x.IsActive })
            .ToListAsync(ct);
        foreach (var eventEntity in eventEntities)
        {
            eventEntity.IsActive = eventEntity.Id == eventId;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new EventSummaryDto(eventAccess.Event.Id, eventAccess.Event.Name, true));
    }

    /// <summary>
    /// Retrieves the Secret Santa configuration and draw state for the event.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Secret Santa administrative state.</returns>
    [HttpGet("{eventId}/admin/secret-santa/state")]
    public async Task<ActionResult<EventAdminSecretSantaStateDto>> AdminGetSecretSantaState([FromRoute] string eventId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        return Ok(await BuildAdminSecretSantaStateDtoAsync(eventId, null, ct));
    }

    /// <summary>
    /// Triggers the Secret Santa draw for all members in the event.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="request">The draw configuration (optional).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated Secret Santa state after the draw.</returns>
    [HttpPost("{eventId}/admin/secret-santa/draw")]
    public async Task<ActionResult<EventAdminSecretSantaStateDto>> AdminDrawSecretSanta([FromRoute] string eventId, [FromBody] CreateEventSecretSantaDrawRequest? request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var participants = await LoadEventSecretSantaParticipantsAsync(eventId, ct);
        var secretSantaEventCode = NormalizeSecretSantaEventCode(eventId, request?.EventCode);
        var drawResult = await _secretSanta.ExecuteDrawAsync(secretSantaEventCode, participants, HttpContext.GetUserId(), ct);

        if (!drawResult.IsSuccess)
        {
            return BadRequest(drawResult.ErrorMessage);
        }

        return Ok(await BuildAdminSecretSantaStateDtoAsync(eventId, secretSantaEventCode, ct));
    }

    /// <summary>
    /// Retrieves a paged list of award category proposals.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="status">Optional status filter (pending, approved, rejected).</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged result of category proposals.</returns>
    [HttpGet("{eventId}/admin/category-proposals")]
    public async Task<ActionResult<PagedResult<CategoryProposalDto>>> AdminGetCategoryProposals(
        [FromRoute] string eventId,
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var normalizedStatus = NormalizeProposalStatusFilter(status);
        var proposalsQuery = _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            proposalsQuery = proposalsQuery.Where(x => x.Status == normalizedStatus);
        }

        var totalCount = await proposalsQuery.CountAsync(ct);
        var pagedProposalDtos = await proposalsQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .Select(x => new CategoryProposalDto(
                x.Id,
                x.Name,
                x.Description,
                x.Status,
                new DateTimeOffset(x.CreatedAtUtc, TimeSpan.Zero)))
            .ToListAsync(ct);

        return new PagedResult<CategoryProposalDto>(pagedProposalDtos, totalCount, skip, take, skip + pagedProposalDtos.Count < totalCount);
    }

    /// <summary>
    /// Updates or approves/rejects a category proposal.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="proposalId">The proposal identifier.</param>
    /// <param name="request">The update details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated category proposal.</returns>
    [HttpPatch("{eventId}/admin/category-proposals/{proposalId}")]
    [HttpPut("{eventId}/admin/category-proposals/{proposalId}")]
    public async Task<ActionResult<CategoryProposalDto>> AdminUpdateCategoryProposal([FromRoute] string eventId, [FromRoute] string proposalId, [FromBody] UpdateAdminCategoryProposalRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;
        
        var proposal = await _awardService.UpdateCategoryProposalAsync(eventId, proposalId, request, ct);
        if (proposal is null) return NotFound();

        return Ok(proposal);
    }

    /// <summary>
    /// Deletes a category proposal.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="proposalId">The proposal identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{eventId}/admin/category-proposals/{proposalId}")]
    public async Task<ActionResult> AdminDeleteCategoryProposal([FromRoute] string eventId, [FromRoute] string proposalId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;
        if (string.IsNullOrWhiteSpace(proposalId)) return BadRequest("proposalId is required.");

        var proposal = await FindCategoryProposalAsync(eventId, proposalId, ct);
        if (proposal is null) return NotFound();

        _db.CategoryProposals.Remove(proposal);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Retrieves a paged list of gala measure proposals.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged result of measure proposals.</returns>
    [HttpGet("{eventId}/admin/measure-proposals")]
    public async Task<ActionResult<PagedResult<MeasureProposalDto>>> AdminGetMeasureProposals(
        [FromRoute] string eventId,
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var normalizedStatus = NormalizeProposalStatusFilter(status);
        var measureProposalsQuery = _db.MeasureProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            measureProposalsQuery = measureProposalsQuery.Where(x => x.Status == normalizedStatus);
        }

        var totalCount = await measureProposalsQuery.CountAsync(ct);
        var pagedMeasureProposalDtos = await measureProposalsQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .Select(x => new MeasureProposalDto(
                x.Id,
                x.Text,
                x.Status,
                new DateTimeOffset(x.CreatedAtUtc, TimeSpan.Zero)))
            .ToListAsync(ct);

        return new PagedResult<MeasureProposalDto>(pagedMeasureProposalDtos, totalCount, skip, take, skip + pagedMeasureProposalDtos.Count < totalCount);
    }

    /// <summary>
    /// Updates the text or status of a gala measure proposal.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="proposalId">The proposal identifier.</param>
    /// <param name="request">The update request details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated measure proposal.</returns>
    [HttpPatch("{eventId}/admin/measure-proposals/{proposalId}")]
    [HttpPut("{eventId}/admin/measure-proposals/{proposalId}")]
    public async Task<ActionResult<MeasureProposalDto>> AdminUpdateMeasureProposal([FromRoute] string eventId, [FromRoute] string proposalId, [FromBody] UpdateMeasureProposalRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;
        if (string.IsNullOrWhiteSpace(proposalId)) return BadRequest("proposalId is required.");

        var proposal = await FindMeasureProposalAsync(eventId, proposalId, ct);
        if (proposal is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            proposal.Text = request.Text.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var normalizedStatus = NormalizeProposalStatus(request.Status);
            if (normalizedStatus is null)
            {
                return BadRequest("Invalid status. Use pending, approved or rejected.");
            }

            proposal.Status = normalizedStatus;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ToMeasureProposalDto(proposal));
    }

    /// <summary>
    /// Deletes a measure proposal.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="proposalId">The proposal identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{eventId}/admin/measure-proposals/{proposalId}")]
    public async Task<ActionResult> AdminDeleteMeasureProposal([FromRoute] string eventId, [FromRoute] string proposalId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var proposal = await FindMeasureProposalAsync(eventId, proposalId, ct);
        if (proposal is null) return NotFound();

        _db.MeasureProposals.Remove(proposal);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Approves a measure proposal and automatically creates an active gala measure.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="proposalId">The proposal identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created gala measure.</returns>
    [HttpPost("{eventId}/admin/measure-proposals/{proposalId}/approve")]
    public async Task<ActionResult<GalaMeasureDto>> AdminApproveMeasureProposal([FromRoute] string eventId, [FromRoute] string proposalId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var proposal = await FindMeasureProposalAsync(eventId, proposalId, ct);
        if (proposal is null) return NotFound();

        proposal.Status = ProposalStatus.Approved;

        var galaMeasure = new GalaMeasureEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            Text = proposal.Text,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Measures.Add(galaMeasure);
        await _db.SaveChangesAsync(ct);

        return Ok(new GalaMeasureDto(
            galaMeasure.Id,
            galaMeasure.Text,
            galaMeasure.IsActive,
            new DateTimeOffset(galaMeasure.CreatedAtUtc, TimeSpan.Zero)
        ));
    }

    /// <summary>
    /// Rejects a measure proposal.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="proposalId">The proposal identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated measure proposal with rejected status.</returns>
    [HttpPost("{eventId}/admin/measure-proposals/{proposalId}/reject")]
    public async Task<ActionResult<MeasureProposalDto>> AdminRejectMeasureProposal([FromRoute] string eventId, [FromRoute] string proposalId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var proposal = await FindMeasureProposalAsync(eventId, proposalId, ct);
        if (proposal is null) return NotFound();

        proposal.Status = ProposalStatus.Rejected;
        await _db.SaveChangesAsync(ct);

        return Ok(ToMeasureProposalDto(proposal));
    }

    /// <summary>
    /// Assigns a nominee to a specific award category.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="nomineeId">The nominee identifier.</param>
    /// <param name="request">The category assignment request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated administrative nominee DTO.</returns>
    [HttpPost("{eventId}/admin/nominations/{nomineeId}/set-category")]
    public async Task<ActionResult<AdminNomineeDto>> AdminSetNominationCategory([FromRoute] string eventId, [FromRoute] string nomineeId, [FromBody] SetNomineeCategoryRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var nominee = await _awardService.SetNominationCategoryAsync(eventId, nomineeId, request.CategoryId, ct);
        if (nominee is null) return NotFound();

        return Ok(nominee);
    }

    /// <summary>
    /// Approves a nomination for the voting phase.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="nomineeId">The nominee identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The approved nominee details.</returns>
    [HttpPost("{eventId}/admin/nominations/{nomineeId}/approve")]
    public async Task<ActionResult<AdminNomineeDto>> AdminApproveNomination([FromRoute] string eventId, [FromRoute] string nomineeId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var nominee = await _awardService.ApproveNominationAsync(eventId, nomineeId, ct);
        if (nominee is null) return NotFound();

        return Ok(nominee);
    }

    /// <summary>
    /// Rejects a nomination.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="nomineeId">The nominee identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rejected nominee details.</returns>
    [HttpPost("{eventId}/admin/nominations/{nomineeId}/reject")]
    public async Task<ActionResult<AdminNomineeDto>> AdminRejectNomination([FromRoute] string eventId, [FromRoute] string nomineeId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var nominee = await _awardService.RejectNominationAsync(eventId, nomineeId, ct);
        if (nominee is null) return NotFound();

        return Ok(nominee);
    }

    // ---------- Paginated endpoints ----------

    /// <summary>
    /// Retrieves a paged audit trail of all votes cast in the event.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="skip">Number of votes to skip.</param>
    /// <param name="take">Number of votes to take.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged result of vote audit rows.</returns>
    [HttpGet("{eventId}/admin/votes/paged")]
    public async Task<ActionResult<AdminVotesPagedDto>> AdminVotesPaged(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        return Ok(await BuildAdminVotesPagedDtoAsync(eventId, skip, take, ct));
    }

    /// <summary>
    /// Retrieves a paged list of nominations for moderation.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged result of nominations.</returns>
    [HttpGet("{eventId}/admin/nominations/paged")]
    public async Task<ActionResult<AdminNomineesPagedDto>> AdminNominationsPaged(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);
        var normalizedStatus = NormalizeNomineeStatusFilter(status);

        return Ok(await BuildAdminNominationsPagedDtoAsync(eventId, normalizedStatus, skip, take, ct));
    }

    /// <summary>
    /// Retrieves a paged list of event members.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="skip">Number of members to skip.</param>
    /// <param name="take">Number of members to take.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged result of public user DTOs.</returns>
    [HttpGet("{eventId}/admin/members/paged")]
    public async Task<ActionResult<PagedResult<PublicUserDto>>> AdminMembersPaged(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        return Ok(await LoadAdminMembersPagedAsync(eventId, skip, take, ct));
    }

    /// <summary>
    /// Retrieves paged official results for award categories. Cached for 30 seconds.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="skip">Number of categories to skip.</param>
    /// <param name="take">Number of categories to take.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged result of category results including vote tallies.</returns>
    [HttpGet("{eventId}/admin/official-results/paged")]
    public async Task<ActionResult<PagedResult<AdminCategoryResultDto>>> AdminOfficialResultsPaged(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var cacheKey = $"AdminOfficialResults_{eventId}_{skip}_{take}";
        if (_cache.TryGetValue(cacheKey, out PagedResult<AdminCategoryResultDto>? cachedOfficialResults))
        {
            return Ok(cachedOfficialResults);
        }

        var officialResults = await BuildAdminOfficialResultsPagedDtoAsync(eventId, skip, take, ct);
        _cache.Set(cacheKey, officialResults, TimeSpan.FromSeconds(30));

        return Ok(officialResults);
    }

    /// <summary>
    /// Retrieves a summary list of award categories.
    /// </summary>
    [HttpGet("{eventId}/admin/categories/summary")]
    public async Task<ActionResult<List<AwardCategorySummaryDto>>> AdminCategoriesSummary(
        [FromRoute] string eventId,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        return Ok(await LoadAdminCategoriesSummaryAsync(eventId, ct));
    }

    /// <summary>
    /// Retrieves a summary list of nominees for the specified event and status.
    /// </summary>
    [HttpGet("{eventId}/admin/nominees/summary")]
    public async Task<ActionResult<List<NomineeSummaryDto>>> AdminNomineesSummary(
        [FromRoute] string eventId,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var normalizedStatus = NormalizeNomineeStatusFilter(status);
        return Ok(await LoadAdminNomineesSummaryAsync(eventId, normalizedStatus, ct));
    }

    /// <summary>
    /// Retrieves a summary list of nominations for the specified event and status.
    /// </summary>
    [HttpGet("{eventId}/admin/nominations/summary")]
    public async Task<ActionResult<List<AdminNomineeSummaryDto>>> AdminNominationsSummary(
        [FromRoute] string eventId,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } accessError) return accessError;

        var normalizedStatus = NormalizeNomineeStatusFilter(status);
        return Ok(await LoadAdminNominationsSummaryAsync(eventId, normalizedStatus, ct));
    }
    private async Task<List<EventSummaryDto>> LoadEventSummariesAsync(CancellationToken ct)
    {
        return await _db.Events
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .Select(x => new EventSummaryDto(x.Id, x.Name, x.IsActive))
            .ToListAsync(ct);
    }

    private async Task<EventAdminStateDto> BuildAdminStateDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        var legacyState = await GetOrCreateEventStateAsync(eventId, ct);
        var eventPhases = await LoadEventPhasesAsync(eventId, ct);
        var memberModuleAccess = await EventModuleAccessEvaluator.EvaluateAsync(
            _db,
            eventId,
            Guid.Empty,
            isAdmin: false,
            eventPhases,
            ct);

        var activePhaseEntity = memberModuleAccess.ActivePhase ?? eventPhases.FirstOrDefault(x => x.IsActive);
        var activeEventPhaseDto = activePhaseEntity is null ? null : ToEventPhaseDto(activePhaseEntity);

        return new EventAdminStateDto(
            eventId,
            activeEventPhaseDto,
            eventPhases.Select(ToEventPhaseDto).ToList(),
            legacyState.NominationsVisible,
            legacyState.ResultsVisible,
            memberModuleAccess.ModuleVisibility,
            memberModuleAccess.EffectiveModules,
            await BuildAdminCountsDtoAsync(eventId, ct)
        );
    }

    private async Task<EventAdminSecretSantaStateDto> BuildAdminSecretSantaStateDtoAsync(
        string eventId,
        string? requestedEventCode,
        CancellationToken ct)
    {
        var latestSecretSantaDraw = string.IsNullOrWhiteSpace(requestedEventCode)
            ? await GetLatestSecretSantaDrawAsync(eventId, ct)
            : await _db.SecretSantaDraws
                .AsNoTracking()
                .Where(x => x.EventCode == requestedEventCode)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

        var secretSantaEventCode = latestSecretSantaDraw?.EventCode ?? NormalizeSecretSantaEventCode(eventId, requestedEventCode);
        var totalMemberCount = await _db.EventMembers.AsNoTracking().CountAsync(x => x.EventId == eventId, ct);
        var totalAssignmentCount = latestSecretSantaDraw is null
            ? 0
            : await _db.SecretSantaAssignments.AsNoTracking().CountAsync(x => x.DrawId == latestSecretSantaDraw.Id, ct);

        return new EventAdminSecretSantaStateDto(
            eventId,
            secretSantaEventCode,
            latestSecretSantaDraw is not null,
            latestSecretSantaDraw?.Id,
            latestSecretSantaDraw is null ? null : new DateTimeOffset(latestSecretSantaDraw.CreatedAtUtc, TimeSpan.Zero),
            latestSecretSantaDraw?.IsLocked ?? false,
            totalMemberCount,
            totalAssignmentCount
        );
    }

    private async Task<EventAdminBootstrapDto> BuildAdminBootstrapDtoAsync(
        string eventId,
        bool includeLists,
        CancellationToken ct)
    {
        var allEventSummaries = await LoadEventSummariesAsync(ct);
        var adminState = await BuildAdminStateDtoAsync(eventId, ct);
        var listCounts = await BuildAdminListCountsDtoAsync(eventId, ct);

        return new EventAdminBootstrapDto(
            allEventSummaries,
            adminState,
            listCounts
        );
    }

    private async Task<AdminListCountsDto> BuildAdminListCountsDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        // Sequential execution to avoid DbContext threading issues
        var awardCategoryIds = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var proposalsSummary = await _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Pending = g.Count(x => x.Status == ProposalStatus.Pending)
            })
            .FirstOrDefaultAsync(ct);

        var measureProposalsSummary = await _db.MeasureProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Pending = g.Count(x => x.Status == ProposalStatus.Pending)
            })
            .FirstOrDefaultAsync(ct);

        var totalNomineesCount = await _db.Nominees
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var totalMembersCount = await _db.EventMembers
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var voteCategoriesCount = await _db.AwardCategories
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId && x.Kind == AwardCategoryKind.UserVote, ct);

        var totalVotesCount = awardCategoryIds.Count == 0
            ? 0
            : await _db.Votes
                .AsNoTracking()
                .CountAsync(x => awardCategoryIds.Contains(x.CategoryId), ct);

        return new AdminListCountsDto(
            totalNomineesCount,
            totalNomineesCount,
            totalVotesCount,
            proposalsSummary?.Total ?? 0,
            proposalsSummary?.Pending ?? 0,
            measureProposalsSummary?.Total ?? 0,
            measureProposalsSummary?.Pending ?? 0,
            totalMembersCount,
            voteCategoriesCount
        );
    }

    private async Task<EventCountsDto> BuildAdminCountsDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        // Reuse the consolidated single-query counts from LoadEventCountsAsync,
        // then fetch the category count separately (different filter).
        var aggregateCounts = await LoadEventCountsAsync(eventId, ct);
        var totalCategoryCount = await _db.AwardCategories
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        return new EventCountsDto(
            aggregateCounts.MemberCount,
            aggregateCounts.HubPostCount,
            totalCategoryCount,
            aggregateCounts.PendingCategoryProposalsCount,
            aggregateCounts.WishlistCount
        );
    }

    private async Task<List<AwardCategoryDto>> LoadAdminCategoriesAsync(
        string eventId,
        CancellationToken ct)
    {
        return await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new AwardCategoryDto(
                x.Id,
                x.Name,
                x.SortOrder,
                x.IsActive,
                x.Kind.ToString(),
                x.Description,
                x.VoteQuestion,
                x.VoteRules))
            .ToListAsync(ct);
    }

    private async Task<AdminNomineeDto> BuildAdminNominationDtoAsync(
        NomineeEntity entity,
        CancellationToken ct)
    {
        var submittedByUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == entity.SubmittedByUserId, ct);

        return ToAdminNomineeDto(entity, submittedByUser);
    }

    // ---------- Paginated methods ----------

    private async Task<AdminVotesPagedDto> BuildAdminVotesPagedDtoAsync(
        string eventId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var awardCategoryIds = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var totalVotesCount = awardCategoryIds.Count == 0
            ? 0
            : await _db.Votes
                .AsNoTracking()
                .CountAsync(x => awardCategoryIds.Contains(x.CategoryId), ct);

        if (totalVotesCount == 0)
        {
            return new AdminVotesPagedDto(0, [], skip, take, false);
        }

        var pagedVotes = await _db.Votes
            .AsNoTracking()
            .Where(x => awardCategoryIds.Contains(x.CategoryId))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var awardCategoriesLookup = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => awardCategoryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        if (pagedVotes.Count == 0)
        {
            return new AdminVotesPagedDto(totalVotesCount, [], skip, take, false);
        }

        // Resolve user names in a single batch
        var voterUserIds = pagedVotes.Select(x => x.UserId).Distinct().ToList();
        var votersLookup = await _db.Users
            .AsNoTracking()
            .Where(x => voterUserIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var nomineeIds = pagedVotes.Select(x => x.NomineeId).Distinct().ToList();
        var nomineesLookup = await _db.Nominees
            .AsNoTracking()
            .Where(x => nomineeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var enrichedVotes = pagedVotes.Select(vote =>
        {
            var categoryName = awardCategoriesLookup.TryGetValue(vote.CategoryId, out var cat)
                ? cat.Name
                : vote.CategoryId;

            var userName = votersLookup.TryGetValue(vote.UserId, out var user)
                ? GetUserName(user)
                : vote.UserId.ToString();

            var nomineeName = nomineesLookup.TryGetValue(vote.NomineeId, out var nom)
                ? nom.Title
                : vote.NomineeId;

            return new AdminVoteAuditRowDto(
                vote.CategoryId,
                categoryName,
                vote.NomineeId,
                nomineeName,
                vote.UserId,
                userName,
                new DateTimeOffset(vote.UpdatedAtUtc, TimeSpan.Zero)
            );
        }).ToList();

        return new AdminVotesPagedDto(
            totalVotesCount,
            enrichedVotes,
            skip,
            take,
            skip + enrichedVotes.Count < totalVotesCount
        );
    }

    private async Task<AdminNomineesPagedDto> BuildAdminNominationsPagedDtoAsync(
        string eventId,
        string? normalizedStatus,
        int skip,
        int take,
        CancellationToken ct)
    {
        var nominationsQuery = _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            nominationsQuery = nominationsQuery.Where(x => x.Status == normalizedStatus);
        }

        var totalNominationsCount = await nominationsQuery.CountAsync(ct);

        var pagedNominees = await nominationsQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        if (pagedNominees.Count == 0)
        {
            return new AdminNomineesPagedDto(totalNominationsCount, [], skip, take, false);
        }

        var submittedByUserIds = pagedNominees
            .Select(x => x.SubmittedByUserId)
            .Distinct()
            .ToList();

        var usersLookup = await _db.Users
            .AsNoTracking()
            .Where(x => submittedByUserIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var nominationDtos = pagedNominees.Select(entity =>
        {
            usersLookup.TryGetValue(entity.SubmittedByUserId, out var user);
            return ToAdminNomineeDto(entity, user);
        }).ToList();

        return new AdminNomineesPagedDto(
            totalNominationsCount,
            nominationDtos,
            skip,
            take,
            skip + nominationDtos.Count < totalNominationsCount
        );
    }

    private async Task<PagedResult<PublicUserDto>> LoadAdminMembersPagedAsync(
        string eventId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var totalCount = await _db.EventMembers
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        if (totalCount == 0)
            return new PagedResult<PublicUserDto>([], 0, skip, take, false);

        var pagedMembers = await _db.EventMembers
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Join(_db.Users.AsNoTracking(),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { User = u, Role = m.Role })
            .OrderByDescending(x => x.Role == EventRoles.Admin)
            .ThenBy(x => x.User.DisplayName ?? x.User.Email)
            .Skip(skip)
            .Take(take)
            .Select(x => new PublicUserDto(
                x.User.Id,
                x.User.Email,
                x.User.DisplayName,
                x.Role == EventRoles.Admin))
            .ToListAsync(ct);

        return new PagedResult<PublicUserDto>(
            pagedMembers,
            totalCount,
            skip,
            take,
            skip + pagedMembers.Count < totalCount
        );
    }

    private async Task<PagedResult<AdminCategoryResultDto>> BuildAdminOfficialResultsPagedDtoAsync(
        string eventId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var memberDirectory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var totalMembersInEvent = memberDirectory.Members.Count;

        var resultsQuery = _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Kind == AwardCategoryKind.UserVote)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name);

        var totalCategoriesCount = await resultsQuery.CountAsync(ct);

        var pagedResultsData = await resultsQuery
            .Skip(skip)
            .Take(take)
            .Select(c => new
            {
                c.Id,
                c.Name,
                Votes = _db.UserVotes
                    .Where(v => v.CategoryId == c.Id)
                    .GroupBy(v => v.TargetUserId)
                    .Select(g => new
                    {
                        TargetUserId = g.Key,
                        VoteCount = g.Count(),
                        Voters = g.Select(v => _db.Users
                            .Where(u => u.Id == v.VoterUserId)
                            .Select(u => u.DisplayName ?? u.Email)
                            .FirstOrDefault())
                            .ToList()
                    })
                    .ToList()
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var categoryResultDtos = pagedResultsData.Select(c =>
        {
            var voteTallies = c.Votes.Select(v => new AdminNomineeVoteTallyDto(
                v.TargetUserId.ToString(),
                memberDirectory.UsersById.TryGetValue(v.TargetUserId, out var user) ? GetUserName(user) : v.TargetUserId.ToString(),
                null,
                v.VoteCount,
                v.Voters.Where(name => name != null).Cast<string>().ToList()
            ))
            .OrderByDescending(n => n.VoteCount)
            .ToList();

            var totalVotesInCategory = voteTallies.Sum(n => n.VoteCount);
            return new AdminCategoryResultDto(
                c.Id,
                c.Name,
                totalVotesInCategory,
                voteTallies,
                totalMembersInEvent == 0 ? 0d : (double)totalVotesInCategory / totalMembersInEvent
            );
        }).ToList();

        return new PagedResult<AdminCategoryResultDto>(
            categoryResultDtos,
            totalCategoriesCount,
            skip,
            take,
            skip + categoryResultDtos.Count < totalCategoriesCount
        );
    }

    private async Task<List<AwardCategorySummaryDto>> LoadAdminCategoriesSummaryAsync(
        string eventId,
        CancellationToken ct)
    {
        return await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(c => new AwardCategorySummaryDto(
                c.Id,
                c.Name,
                c.SortOrder,
                c.IsActive,
                c.Kind.ToString()
            ))
            .ToListAsync(ct);
    }

    private async Task<List<NomineeSummaryDto>> LoadAdminNomineesSummaryAsync(
        string eventId,
        string? normalizedStatus,
        CancellationToken ct)
    {
        var summariesQuery = _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            summariesQuery = summariesQuery.Where(x => x.Status == normalizedStatus);
        }

        return await summariesQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(n => new NomineeSummaryDto(
                n.Id,
                n.CategoryId,
                n.Title,
                n.Status
            ))
            .ToListAsync(ct);
    }

    private async Task<List<AdminNomineeSummaryDto>> LoadAdminNominationsSummaryAsync(
        string eventId,
        string? normalizedStatus,
        CancellationToken ct)
    {
        var nominationsSummaryQuery = _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            nominationsSummaryQuery = nominationsSummaryQuery.Where(x => x.Status == normalizedStatus);
        }

        var pagedNominationsSummary = await (
            from nominee in nominationsSummaryQuery
            join user in _db.Users.AsNoTracking() on nominee.SubmittedByUserId equals user.Id into userGroup
            from user in userGroup.DefaultIfEmpty()
            orderby nominee.CreatedAtUtc descending
            select new
            {
                nominee.Id,
                nominee.CategoryId,
                nominee.Title,
                nominee.Status,
                nominee.SubmittedByUserId,
                DisplayName = user == null ? null : user.DisplayName,
                Email = user == null ? null : user.Email
            })
            .ToListAsync(ct);

        return pagedNominationsSummary.Select(entry => new AdminNomineeSummaryDto(
                entry.Id,
                entry.CategoryId,
                entry.Title,
                entry.Status,
                entry.SubmittedByUserId,
                string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? entry.Email ?? entry.SubmittedByUserId.ToString()
                    : entry.DisplayName
            ))
            .ToList();
    }
}



