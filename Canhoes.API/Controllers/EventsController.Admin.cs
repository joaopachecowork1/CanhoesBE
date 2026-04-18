using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Canhoes.Api.Controllers.EventsControllerMappers;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    [HttpGet("{eventId}/admin/categories")]
    public async Task<ActionResult<List<AwardCategoryDto>>> AdminGetCategories([FromRoute] string eventId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        return Ok(await LoadAdminCategoriesAsync(eventId, ct));
    }

    [HttpPost("{eventId}/admin/categories")]
    public async Task<ActionResult<AwardCategoryDto>> AdminCreateCategory([FromRoute] string eventId, [FromBody] CreateAwardCategoryRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");
        if (!Enum.TryParse<AwardCategoryKind>(request.Kind, true, out var parsedKind))
            return BadRequest("Invalid kind.");

        var category = await CreateCategoryEntityAsync(
            eventId,
            request.Name,
            request.Description,
            request.SortOrder,
            parsedKind,
            ct);

        return Ok(ToAwardCategoryDto(category));
    }

    [HttpPut("{eventId}/admin/categories/{categoryId}")]
    public async Task<ActionResult<AwardCategoryDto>> AdminUpdateCategory([FromRoute] string eventId, [FromRoute] string categoryId, [FromBody] UpdateAwardCategoryRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var category = await FindCategoryAsync(eventId, categoryId, ct);
        if (category is null) return NotFound();
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
            category.Name = normalizedName;
        }

        if (request.Description is not null)
        {
            category.Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim();
        }

        if (request.SortOrder.HasValue)
        {
            category.SortOrder = request.SortOrder.Value;
        }

        if (request.IsActive.HasValue)
        {
            category.IsActive = request.IsActive.Value;
        }

        if (request.Kind is not null)
        {
            category.Kind = parsedKind!.Value;
        }

        if (request.VoteQuestion is not null)
        {
            category.VoteQuestion = string.IsNullOrWhiteSpace(request.VoteQuestion)
                ? null
                : request.VoteQuestion.Trim();
        }

        if (request.VoteRules is not null)
        {
            category.VoteRules = string.IsNullOrWhiteSpace(request.VoteRules)
                ? null
                : request.VoteRules.Trim();
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ToAwardCategoryDto(category));
    }

    [HttpDelete("{eventId}/admin/categories/{categoryId}")]
    public async Task<ActionResult> AdminDeleteCategory([FromRoute] string eventId, [FromRoute] string categoryId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var category = await FindCategoryAsync(eventId, categoryId, ct);
        if (category is null) return NotFound();

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

        _db.AwardCategories.Remove(category);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{eventId}/admin/state")]
    public async Task<ActionResult<EventAdminStateDto>> GetAdminState([FromRoute] string eventId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        return Ok(await BuildAdminStateDtoAsync(eventId, ct));
    }

    [HttpGet("{eventId}/admin/bootstrap")]
    public async Task<ActionResult<EventAdminBootstrapDto>> GetAdminBootstrap(
        [FromRoute] string eventId,
        [FromQuery] bool includeLists = false,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        _ = includeLists;
        return Ok(await BuildAdminBootstrapDtoAsync(eventId, includeLists, ct));
    }

    [HttpPut("{eventId}/admin/state")]
    public async Task<ActionResult<EventAdminStateDto>> UpdateAdminState([FromRoute] string eventId, [FromBody] UpdateEventAdminStateRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

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

    [HttpPatch("{eventId}/modules")]
    public async Task<ActionResult<EventOverviewDto>> UpdateEventModules([FromRoute] string eventId, [FromBody] UpdateEventModulesRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

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

    [HttpPut("{eventId}/admin/phase")]
    public async Task<ActionResult<EventAdminStateDto>> UpdateAdminPhase([FromRoute] string eventId, [FromBody] UpdateEventPhaseRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;
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

    [HttpPut("{eventId}/admin/activate")]
    public async Task<ActionResult<EventSummaryDto>> ActivateEvent([FromRoute] string eventId, CancellationToken ct)
    {
        var (access, error) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (error is not null) return error;

        var events = await _db.Events.ToListAsync(ct);
        foreach (var eventEntity in events)
        {
            eventEntity.IsActive = eventEntity.Id == eventId;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new EventSummaryDto(access.Event.Id, access.Event.Name, true));
    }

    [HttpGet("{eventId}/admin/secret-santa/state")]
    public async Task<ActionResult<EventAdminSecretSantaStateDto>> AdminGetSecretSantaState([FromRoute] string eventId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        return Ok(await BuildAdminSecretSantaStateDtoAsync(eventId, null, ct));
    }

    [HttpPost("{eventId}/admin/secret-santa/draw")]
    public async Task<ActionResult<EventAdminSecretSantaStateDto>> AdminDrawSecretSanta([FromRoute] string eventId, [FromBody] CreateEventSecretSantaDrawRequest? request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var participants = await LoadEventSecretSantaParticipantsAsync(eventId, ct);
        if (participants.Count < 2)
        {
            return BadRequest("Need at least 2 members to draw.");
        }

        var eventCode = NormalizeSecretSantaEventCode(eventId, request?.EventCode);
        var result = await _secretSanta.ExecuteDrawAsync(_db, eventCode, participants, HttpContext.GetUserId(), ct);

        if (!result.IsSuccess)
        {
            return BadRequest(result.ErrorMessage);
        }

        return Ok(await BuildAdminSecretSantaStateDtoAsync(eventId, eventCode, ct));
    }

    [HttpGet("{eventId}/admin/category-proposals")]
    public async Task<ActionResult<PagedResult<CategoryProposalDto>>> AdminGetCategoryProposals(
        [FromRoute] string eventId,
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var normalizedStatus = NormalizeProposalStatusFilter(status);
        var query = _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        var total = await query.CountAsync(ct);
        var proposals = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var dtos = proposals.Select(ToCategoryProposalDto).ToList();
        return new PagedResult<CategoryProposalDto>(dtos, total, skip, take, skip + dtos.Count < total);
    }

    [HttpPatch("{eventId}/admin/category-proposals/{proposalId}")]
    [HttpPut("{eventId}/admin/category-proposals/{proposalId}")]
    public async Task<ActionResult<CategoryProposalDto>> AdminUpdateCategoryProposal([FromRoute] string eventId, [FromRoute] string proposalId, [FromBody] UpdateAdminCategoryProposalRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;
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

    [HttpDelete("{eventId}/admin/category-proposals/{proposalId}")]
    public async Task<ActionResult> AdminDeleteCategoryProposal([FromRoute] string eventId, [FromRoute] string proposalId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;
        if (string.IsNullOrWhiteSpace(proposalId)) return BadRequest("proposalId is required.");

        var proposal = await FindCategoryProposalAsync(eventId, proposalId, ct);
        if (proposal is null) return NotFound();

        _db.CategoryProposals.Remove(proposal);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{eventId}/admin/measure-proposals")]
    public async Task<ActionResult<PagedResult<MeasureProposalDto>>> AdminGetMeasureProposals(
        [FromRoute] string eventId,
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var normalizedStatus = NormalizeProposalStatusFilter(status);
        var query = _db.MeasureProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        var total = await query.CountAsync(ct);
        var proposals = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var dtos = proposals.Select(ToMeasureProposalDto).ToList();
        return new PagedResult<MeasureProposalDto>(dtos, total, skip, take, skip + dtos.Count < total);
    }

    [HttpPatch("{eventId}/admin/measure-proposals/{proposalId}")]
    [HttpPut("{eventId}/admin/measure-proposals/{proposalId}")]
    public async Task<ActionResult<MeasureProposalDto>> AdminUpdateMeasureProposal([FromRoute] string eventId, [FromRoute] string proposalId, [FromBody] UpdateMeasureProposalRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;
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

    [HttpDelete("{eventId}/admin/measure-proposals/{proposalId}")]
    public async Task<ActionResult> AdminDeleteMeasureProposal([FromRoute] string eventId, [FromRoute] string proposalId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var proposal = await FindMeasureProposalAsync(eventId, proposalId, ct);
        if (proposal is null) return NotFound();

        _db.MeasureProposals.Remove(proposal);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{eventId}/admin/measure-proposals/{proposalId}/approve")]
    public async Task<ActionResult<GalaMeasureDto>> AdminApproveMeasureProposal([FromRoute] string eventId, [FromRoute] string proposalId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var proposal = await FindMeasureProposalAsync(eventId, proposalId, ct);
        if (proposal is null) return NotFound();

        proposal.Status = ProposalStatus.Approved;

        var measure = new GalaMeasureEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            Text = proposal.Text,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Measures.Add(measure);
        await _db.SaveChangesAsync(ct);

        return Ok(new GalaMeasureDto(
            measure.Id,
            measure.Text,
            measure.IsActive,
            new DateTimeOffset(measure.CreatedAtUtc, TimeSpan.Zero)
        ));
    }

    [HttpPost("{eventId}/admin/measure-proposals/{proposalId}/reject")]
    public async Task<ActionResult<MeasureProposalDto>> AdminRejectMeasureProposal([FromRoute] string eventId, [FromRoute] string proposalId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var proposal = await FindMeasureProposalAsync(eventId, proposalId, ct);
        if (proposal is null) return NotFound();

        proposal.Status = ProposalStatus.Rejected;
        await _db.SaveChangesAsync(ct);

        return Ok(ToMeasureProposalDto(proposal));
    }

    [HttpPost("{eventId}/admin/nominations/{nomineeId}/set-category")]
    public async Task<ActionResult<AdminNomineeDto>> AdminSetNominationCategory([FromRoute] string eventId, [FromRoute] string nomineeId, [FromBody] SetNomineeCategoryRequest request, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

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

    [HttpPost("{eventId}/admin/nominations/{nomineeId}/approve")]
    public async Task<ActionResult<AdminNomineeDto>> AdminApproveNomination([FromRoute] string eventId, [FromRoute] string nomineeId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var nominee = await FindNomineeAsync(eventId, nomineeId, ct);
        if (nominee is null) return NotFound();
        if (string.IsNullOrWhiteSpace(nominee.CategoryId))
            return BadRequest("Nominee must have a category before approval.");

        nominee.Status = ProposalStatus.Approved;
        await _db.SaveChangesAsync(ct);

        return Ok(await BuildAdminNominationDtoAsync(nominee, ct));
    }

    [HttpPost("{eventId}/admin/nominations/{nomineeId}/reject")]
    public async Task<ActionResult<AdminNomineeDto>> AdminRejectNomination([FromRoute] string eventId, [FromRoute] string nomineeId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var nominee = await FindNomineeAsync(eventId, nomineeId, ct);
        if (nominee is null) return NotFound();

        nominee.Status = ProposalStatus.Rejected;
        await _db.SaveChangesAsync(ct);

        return Ok(await BuildAdminNominationDtoAsync(nominee, ct));
    }

    // ---------- Paginated endpoints ----------

    [HttpGet("{eventId}/admin/votes/paged")]
    public async Task<ActionResult<AdminVotesPagedDto>> AdminVotesPaged(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        return Ok(await BuildAdminVotesPagedDtoAsync(eventId, skip, take, ct));
    }

    [HttpGet("{eventId}/admin/nominations/paged")]
    public async Task<ActionResult<AdminNomineesPagedDto>> AdminNominationsPaged(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);
        var normalizedStatus = NormalizeNomineeStatusFilter(status);

        return Ok(await BuildAdminNominationsPagedDtoAsync(eventId, normalizedStatus, skip, take, ct));
    }

    [HttpGet("{eventId}/admin/members/paged")]
    public async Task<ActionResult<PagedResult<PublicUserDto>>> AdminMembersPaged(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        return Ok(await LoadAdminMembersPagedAsync(eventId, skip, take, ct));
    }

    [HttpGet("{eventId}/admin/official-results/paged")]
    public async Task<ActionResult<PagedResult<AdminCategoryResultDto>>> AdminOfficialResultsPaged(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        return Ok(await BuildAdminOfficialResultsPagedDtoAsync(eventId, skip, take, ct));
    }

    [HttpGet("{eventId}/admin/categories/summary")]
    public async Task<ActionResult<List<AwardCategorySummaryDto>>> AdminCategoriesSummary(
        [FromRoute] string eventId,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        return Ok(await LoadAdminCategoriesSummaryAsync(eventId, ct));
    }

    [HttpGet("{eventId}/admin/nominees/summary")]
    public async Task<ActionResult<List<NomineeSummaryDto>>> AdminNomineesSummary(
        [FromRoute] string eventId,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var normalizedStatus = NormalizeNomineeStatusFilter(status);
        return Ok(await LoadAdminNomineesSummaryAsync(eventId, normalizedStatus, ct));
    }

    [HttpGet("{eventId}/admin/nominations/summary")]
    public async Task<ActionResult<List<AdminNomineeSummaryDto>>> AdminNominationsSummary(
        [FromRoute] string eventId,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var normalizedStatus = NormalizeNomineeStatusFilter(status);
        return Ok(await LoadAdminNominationsSummaryAsync(eventId, normalizedStatus, ct));
    }
}
