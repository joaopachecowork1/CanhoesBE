using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public async Task<ActionResult<EventAdminBootstrapDto>> GetAdminBootstrap([FromRoute] string eventId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        return Ok(await BuildAdminBootstrapDtoAsync(eventId, ct));
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

        var existingDraws = await _db.SecretSantaDraws
            .Where(x => x.EventCode == eventCode)
            .ToListAsync(ct);

        if (existingDraws.Count > 0)
        {
            foreach (var existingDraw in existingDraws)
            {
                var existingAssignments = _db.SecretSantaAssignments.Where(x => x.DrawId == existingDraw.Id);
                _db.SecretSantaAssignments.RemoveRange(existingAssignments);
            }

            _db.SecretSantaDraws.RemoveRange(existingDraws);
            await _db.SaveChangesAsync(ct);
        }

        var rng = new Random();
        const int maxAttempts = 100;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var shuffledParticipants = participants.ToList();
            for (var index = shuffledParticipants.Count - 1; index > 0; index--)
            {
                var swapIndex = rng.Next(index + 1);
                (shuffledParticipants[index], shuffledParticipants[swapIndex]) =
                    (shuffledParticipants[swapIndex], shuffledParticipants[index]);
            }

            var isValid = true;
            for (var index = 0; index < participants.Count; index++)
            {
                if (participants[index].Id == shuffledParticipants[index].Id)
                {
                    isValid = false;
                    break;
                }
            }

            if (!isValid)
            {
                continue;
            }

            var draw = new SecretSantaDrawEntity
            {
                Id = Guid.NewGuid().ToString(),
                EventCode = eventCode,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = HttpContext.GetUserId(),
                IsLocked = true,
            };

            _db.SecretSantaDraws.Add(draw);

            for (var index = 0; index < participants.Count; index++)
            {
                _db.SecretSantaAssignments.Add(new SecretSantaAssignmentEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    DrawId = draw.Id,
                    GiverUserId = participants[index].Id,
                    ReceiverUserId = shuffledParticipants[index].Id,
                });
            }

            await _db.SaveChangesAsync(ct);
            return Ok(await BuildAdminSecretSantaStateDtoAsync(eventId, eventCode, ct));
        }

        return BadRequest(
            $"Could not create a valid draw after {maxAttempts} attempts. Please try again.");
    }

    [HttpGet("{eventId}/admin/members")]
    public async Task<ActionResult<List<PublicUserDto>>> AdminGetMembers([FromRoute] string eventId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        return Ok(await LoadAdminMembersAsync(eventId, ct));
    }

    [HttpGet("{eventId}/admin/votes")]
    public async Task<ActionResult<AdminVotesDto>> AdminVotes([FromRoute] string eventId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        return Ok(await BuildAdminVotesDtoAsync(eventId, ct));
    }
    [HttpGet("{eventId}/admin/category-proposals")]
    public async Task<ActionResult<List<CategoryProposalDto>>> AdminGetCategoryProposals([FromRoute] string eventId, [FromQuery] string? status, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var normalizedStatus = NormalizeProposalStatusFilter(status);
        var query = _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        var proposals = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(proposals.Select(ToCategoryProposalDto).ToList());
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

    [HttpGet("{eventId}/admin/proposals")]
    public async Task<ActionResult<AdminProposalsHistoryDto>> AdminGetProposalsHistory([FromRoute] string eventId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        return Ok(await BuildAdminProposalsHistoryDtoAsync(eventId, ct));
    }

    [HttpGet("{eventId}/admin/measure-proposals")]
    public async Task<ActionResult<List<MeasureProposalDto>>> AdminGetMeasureProposals([FromRoute] string eventId, [FromQuery] string? status, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var normalizedStatus = NormalizeProposalStatusFilter(status);
        var query = _db.MeasureProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        var proposals = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(proposals.Select(ToMeasureProposalDto).ToList());
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

        proposal.Status = "approved";

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

        proposal.Status = "rejected";
        await _db.SaveChangesAsync(ct);

        return Ok(ToMeasureProposalDto(proposal));
    }

    [HttpGet("{eventId}/admin/nominees")]
    public async Task<ActionResult<List<NomineeDto>>> AdminGetNominees([FromRoute] string eventId, [FromQuery] string? status, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var normalizedStatus = NormalizeNomineeStatusFilter(status);

        return Ok(await LoadAdminNomineeDtosAsync(eventId, normalizedStatus, ct));
    }

    [HttpPost("{eventId}/admin/nominees/{nomineeId}/set-category")]
    public async Task<ActionResult<NomineeDto>> AdminSetNomineeCategory([FromRoute] string eventId, [FromRoute] string nomineeId, [FromBody] SetNomineeCategoryRequest request, CancellationToken ct)
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
        return Ok(ToNomineeDto(nominee));
    }

    [HttpPost("{eventId}/admin/nominees/{nomineeId}/approve")]
    public async Task<ActionResult<NomineeDto>> AdminApproveNominee([FromRoute] string eventId, [FromRoute] string nomineeId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var nominee = await FindNomineeAsync(eventId, nomineeId, ct);
        if (nominee is null) return NotFound();
        if (string.IsNullOrWhiteSpace(nominee.CategoryId))
            return BadRequest("Nominee must have a category before approval.");

        nominee.Status = "approved";
        await _db.SaveChangesAsync(ct);

        return Ok(ToNomineeDto(nominee));
    }

    [HttpPost("{eventId}/admin/nominees/{nomineeId}/reject")]
    public async Task<ActionResult<NomineeDto>> AdminRejectNominee([FromRoute] string eventId, [FromRoute] string nomineeId, CancellationToken ct)
    {
        if (await RequireManageAccessAsync(eventId, ct) is { } error) return error;

        var nominee = await FindNomineeAsync(eventId, nomineeId, ct);
        if (nominee is null) return NotFound();

        nominee.Status = "rejected";
        await _db.SaveChangesAsync(ct);

        return Ok(ToNomineeDto(nominee));
    }
}
