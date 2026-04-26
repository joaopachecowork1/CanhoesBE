using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Models;
using Canhoes.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using static Canhoes.Api.Controllers.EventsControllerMappers;

namespace Canhoes.Api.Controllers;

[Authorize(Policy = "AdminOnly")]
public sealed partial class EventsController
{
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

        return Ok(await LoadAdminCategoriesAsync(eventId, ct));
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
        if (!Enum.TryParse<AwardCategoryKind>(request.Kind, true, out var parsedKind))
            return BadRequest("Invalid kind.");

        var awardCategory = await CreateCategoryEntityAsync(
            eventId,
            request.Name,
            request.Description,
            request.SortOrder,
            parsedKind,
            ct);

        return Ok(ToAwardCategoryDto(awardCategory));
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

        var awardCategory = await FindCategoryAsync(eventId, categoryId, ct);
        if (awardCategory is null) return NotFound();
        
        AwardCategoryKind? parsedKind = null;
        if (request.Kind is not null)
        {
            if (!Enum.TryParse<AwardCategoryKind>(request.Kind, true, out var kindValue))
                return BadRequest("Invalid kind.");

            parsedKind = kindValue;
        }

        if (request.Name is not null)
        {
            var normalizedName = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName)) return BadRequest("Name is required.");
            awardCategory.Name = normalizedName;
        }

        if (request.Description is not null)
        {
            awardCategory.Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim();
        }

        if (request.SortOrder.HasValue)
        {
            awardCategory.SortOrder = request.SortOrder.Value;
        }

        if (request.IsActive.HasValue)
        {
            awardCategory.IsActive = request.IsActive.Value;
        }

        if (request.Kind is not null)
        {
            awardCategory.Kind = parsedKind!.Value;
        }

        if (request.VoteQuestion is not null)
        {
            awardCategory.VoteQuestion = string.IsNullOrWhiteSpace(request.VoteQuestion)
                ? null
                : request.VoteQuestion.Trim();
        }

        if (request.VoteRules is not null)
        {
            awardCategory.VoteRules = string.IsNullOrWhiteSpace(request.VoteRules)
                ? null
                : request.VoteRules.Trim();
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ToAwardCategoryDto(awardCategory));
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

        var awardCategory = await FindCategoryAsync(eventId, categoryId, ct);
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
        var nextModuleVisibility = request.ModuleVisibility ?? ParseModuleVisibility(legacyState);

        if (request.NominationsVisible.HasValue)
        {
            legacyState.NominationsVisible = request.NominationsVisible.Value;
        }

        if (request.ResultsVisible.HasValue)
        {
            legacyState.ResultsVisible = request.ResultsVisible.Value;
        }

        legacyState.ModuleVisibilityJson = SerializeModuleVisibility(nextModuleVisibility);
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
        legacyState.ModuleVisibilityJson = SerializeModuleVisibility(
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
        return await GetEventOverview(eventId, ct);
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
        ApplyLegacyStateForPhase(legacyState, targetPhase.Type);

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
        if (participants.Count < 2)
        {
            return BadRequest("Need at least 2 members to draw.");
        }

        var secretSantaEventCode = NormalizeSecretSantaEventCode(eventId, request?.EventCode);
        var drawResult = await _secretSanta.ExecuteDrawAsync(_db, secretSantaEventCode, participants, HttpContext.GetUserId(), ct);

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
        if (string.IsNullOrWhiteSpace(proposalId)) return BadRequest("proposalId is required.");

        var proposal = await FindCategoryProposalAsync(eventId, proposalId, ct);
        if (proposal is null) return NotFound();

        if (request.Name is not null)
        {
            var normalizedName = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName)) return BadRequest("Name is required.");
            proposal.Name = normalizedName;
        }

        if (request.Description is not null)
        {
            proposal.Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim();
        }

        if (request.Status is not null)
        {
            var normalizedStatus = NormalizeProposalStatus(request.Status);
            if (normalizedStatus is null)
            {
                return BadRequest("Invalid status. Use pending, approved or rejected.");
            }

            await ApplyCategoryProposalStatusAsync(proposal, eventId, normalizedStatus, ct);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ToCategoryProposalDto(proposal));
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

        var nominee = await FindNomineeAsync(eventId, nomineeId, ct);
        if (nominee is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.CategoryId))
        {
            var categoryExists = await _db.AwardCategories
                .AsNoTracking()
                .AnyAsync(x => x.Id == request.CategoryId && x.EventId == eventId, ct);

            if (!categoryExists) return BadRequest("Invalid category.");
        }

        nominee.CategoryId = string.IsNullOrWhiteSpace(request.CategoryId)
            ? null
            : request.CategoryId.Trim();

        await _db.SaveChangesAsync(ct);
        return Ok(await BuildAdminNominationDtoAsync(nominee, ct));
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

        var nominee = await FindNomineeAsync(eventId, nomineeId, ct);
        if (nominee is null) return NotFound();
        if (string.IsNullOrWhiteSpace(nominee.CategoryId))
            return BadRequest("Nominee must have a category before approval.");

        nominee.Status = ProposalStatus.Approved;
        await _db.SaveChangesAsync(ct);

        return Ok(await BuildAdminNominationDtoAsync(nominee, ct));
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

        var nominee = await FindNomineeAsync(eventId, nomineeId, ct);
        if (nominee is null) return NotFound();

        nominee.Status = ProposalStatus.Rejected;
        await _db.SaveChangesAsync(ct);

        return Ok(await BuildAdminNominationDtoAsync(nominee, ct));
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
}
