using Canhoes.Api.Access;
using Canhoes.Api.Models;
using Canhoes.Api.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public partial class CanhoesController
{
    [HttpGet("admin/proposals")]
    public async Task<ActionResult<AdminProposalsHistoryDto>> AdminProposals(CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var catsPending = await _db.CategoryProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId && p.Status == "pending")
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        var catsApproved = await _db.CategoryProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId && p.Status == "approved")
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        var catsRejected = await _db.CategoryProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId && p.Status == "rejected")
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        var measApproved = await _db.MeasureProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId && p.Status == "approved")
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        var measPending = await _db.MeasureProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId && p.Status == "pending")
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        var measRejected = await _db.MeasureProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId && p.Status == "rejected")
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        var dto = new AdminProposalsHistoryDto(
            new ProposalsByStatus<CategoryProposalDto>(
                catsPending.Select(ToCategoryProposalDto),
                catsApproved.Select(ToCategoryProposalDto),
                catsRejected.Select(ToCategoryProposalDto)),
            new ProposalsByStatus<MeasureProposalDto>(
                measPending.Select(ToMeasureProposalDto),
                measApproved.Select(ToMeasureProposalDto),
                measRejected.Select(ToMeasureProposalDto)));

        return Ok(dto);
    }

    [HttpGet("admin/categories")]
    public async Task<ActionResult<List<AwardCategoryDto>>> AdminGetCategories(CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var cats = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == activeEventId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        return Ok(cats.Select(ToAwardCategoryDto).ToList());
    }

    [HttpPatch("admin/categories/{id}")]
    [HttpPut("admin/categories/{id}")]
    public async Task<ActionResult<AwardCategoryDto>> AdminUpdateCategory(
        [FromRoute] string id,
        [FromBody] UpdateAwardCategoryRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required.");
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var cat = await _db.AwardCategories.SingleOrDefaultAsync(x => x.Id == id && x.EventId == activeEventId, ct);
        if (cat is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name)) cat.Name = req.Name.Trim();
        if (req.SortOrder.HasValue) cat.SortOrder = req.SortOrder.Value;
        if (req.IsActive.HasValue) cat.IsActive = req.IsActive.Value;

        if (!string.IsNullOrWhiteSpace(req.Kind) &&
            Enum.TryParse<AwardCategoryKind>(req.Kind, ignoreCase: true, out var kind))
        {
            cat.Kind = kind;
        }

        if (req.Description is not null) cat.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (req.VoteQuestion is not null) cat.VoteQuestion = string.IsNullOrWhiteSpace(req.VoteQuestion) ? null : req.VoteQuestion.Trim();
        if (req.VoteRules is not null) cat.VoteRules = string.IsNullOrWhiteSpace(req.VoteRules) ? null : req.VoteRules.Trim();

        await _db.SaveChangesAsync(ct);
        return Ok(ToAwardCategoryDto(cat));
    }

    [HttpPost("admin/categories")]
    public async Task<ActionResult<AwardCategoryDto>> AdminCreateCategory([FromBody] CreateAwardCategoryRequest req, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required.");
        if (!Enum.TryParse<AwardCategoryKind>(req.Kind, ignoreCase: true, out var kind))
            return BadRequest("Invalid category kind.");

        var maxSort = await _db.AwardCategories
            .Where(c => c.EventId == activeEventId)
            .MaxAsync(c => (int?)c.SortOrder, ct) ?? 0;

        var cat = new AwardCategoryEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = activeEventId,
            Name = req.Name.Trim(),
            SortOrder = req.SortOrder ?? (maxSort + 1),
            Kind = kind,
            IsActive = true,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            VoteQuestion = string.IsNullOrWhiteSpace(req.VoteQuestion) ? null : req.VoteQuestion.Trim(),
            VoteRules = string.IsNullOrWhiteSpace(req.VoteRules) ? null : req.VoteRules.Trim()
        };

        _db.AwardCategories.Add(cat);
        await _db.SaveChangesAsync(ct);
        return ToAwardCategoryDto(cat);
    }

    [HttpGet("admin/pending")]
    public async Task<ActionResult<PendingAdminDto>> AdminPending(CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var nominees = await _db.Nominees.AsNoTracking()
            .Where(n => n.EventId == activeEventId && n.Status == "pending")
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToListAsync(ct);
        var cats = await _db.CategoryProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId && p.Status == "pending")
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);
        var meas = await _db.MeasureProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId && p.Status == "pending")
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        return new PendingAdminDto(
            nominees.Select(ToNomineeDto).ToList(),
            cats.Select(ToCategoryProposalDto).ToList(),
            meas.Select(ToMeasureProposalDto).ToList());
    }

    [HttpPost("admin/nominees/{id}/set-category")]
    public async Task<ActionResult<NomineeDto>> AdminSetNomineeCategory([FromRoute] string id, [FromBody] SetNomineeCategoryRequest req, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var nominee = await FindNomineeAsync(activeEventId, id, ct);
        if (nominee is null) return NotFound();

        nominee.CategoryId = string.IsNullOrWhiteSpace(req.CategoryId) ? null : req.CategoryId;
        await _db.SaveChangesAsync(ct);
        return ToNomineeDto(nominee);
    }

    [HttpPost("admin/categories/{id}/approve")]
    public async Task<ActionResult<AwardCategoryDto>> ApproveCategoryProposal([FromRoute] string id, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var proposal = await FindCategoryProposalAsync(activeEventId, id, ct);
        if (proposal is null) return NotFound();

        proposal.Status = "approved";
        var maxSort = await _db.AwardCategories
            .Where(c => c.EventId == activeEventId)
            .MaxAsync(c => (int?)c.SortOrder, ct) ?? 0;

        var cat = new AwardCategoryEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = activeEventId,
            Name = proposal.Name,
            SortOrder = maxSort + 1,
            IsActive = true
        };

        _db.AwardCategories.Add(cat);
        await _db.SaveChangesAsync(ct);
        return ToAwardCategoryDto(cat);
    }

    [HttpPost("admin/categories/{id}/reject")]
    public async Task<ActionResult<CategoryProposalDto>> RejectCategoryProposal([FromRoute] string id, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var proposal = await FindCategoryProposalAsync(activeEventId, id, ct);
        if (proposal is null) return NotFound();

        proposal.Status = "rejected";
        await _db.SaveChangesAsync(ct);
        return ToCategoryProposalDto(proposal);
    }

    [HttpPost("admin/measures/{id}/approve")]
    public async Task<ActionResult<GalaMeasureDto>> ApproveMeasureProposal([FromRoute] string id, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var proposal = await FindMeasureProposalAsync(activeEventId, id, ct);
        if (proposal is null) return NotFound();

        proposal.Status = "approved";
        var measure = new GalaMeasureEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = activeEventId,
            Text = proposal.Text,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Measures.Add(measure);
        await _db.SaveChangesAsync(ct);
        return ToGalaMeasureDto(measure);
    }

    [HttpGet("admin/measures/proposals")]
    public async Task<ActionResult<List<MeasureProposalDto>>> AdminListMeasureProposals(
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var normalized = string.IsNullOrWhiteSpace(status)
            ? "all"
            : status.Trim().ToLowerInvariant();

        IQueryable<MeasureProposalEntity> query = _db.MeasureProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId);
        if (normalized is "pending" or "approved" or "rejected")
        {
            query = query.Where(p => p.Status == normalized);
        }

        var list = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(list.Select(ToMeasureProposalDto).ToList());
    }

    [HttpPatch("admin/measures/{id}")]
    [HttpPut("admin/measures/{id}")]
    public async Task<ActionResult<MeasureProposalDto>> AdminUpdateMeasureProposal(
        [FromRoute] string id,
        [FromBody] UpdateMeasureProposalRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required.");
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var proposal = await FindMeasureProposalAsync(activeEventId, id, ct);
        if (proposal is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Text))
        {
            proposal.Text = req.Text.Trim();
        }

        if (!string.IsNullOrWhiteSpace(req.Status))
        {
            var statusValue = req.Status.Trim().ToLowerInvariant();
            if (statusValue is not ("pending" or "approved" or "rejected"))
            {
                return BadRequest("Invalid status. Use pending, approved or rejected.");
            }

            proposal.Status = statusValue;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ToMeasureProposalDto(proposal));
    }

    [HttpDelete("admin/measures/{id}")]
    public async Task<IActionResult> AdminDeleteMeasureProposal([FromRoute] string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required.");
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var proposal = await FindMeasureProposalAsync(activeEventId, id, ct);
        if (proposal is null) return NotFound();

        _db.MeasureProposals.Remove(proposal);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("admin/measures/{id}/reject")]
    public async Task<ActionResult<MeasureProposalDto>> RejectMeasureProposal([FromRoute] string id, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var proposal = await FindMeasureProposalAsync(activeEventId, id, ct);
        if (proposal is null) return NotFound();

        proposal.Status = "rejected";
        await _db.SaveChangesAsync(ct);
        return ToMeasureProposalDto(proposal);
    }

    [HttpGet("admin/votes")]
    public async Task<ActionResult<object>> AdminVotes(CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var categoryIds = await LoadEventCategoryIdsAsync(activeEventId, ct);
        var votes = await _db.Votes.AsNoTracking()
            .Where(v => categoryIds.Contains(v.CategoryId))
            .OrderByDescending(v => v.UpdatedAtUtc)
            .ToListAsync(ct);

        return new
        {
            total = votes.Count,
            votes = votes.Select(v => new { v.CategoryId, v.NomineeId, v.UserId, v.UpdatedAtUtc })
        };
    }

    [HttpGet("admin/nominees")]
    public async Task<ActionResult<List<NomineeDto>>> AdminGetAllNominees([FromQuery] string? status, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var query = _db.Nominees.AsNoTracking().Where(n => n.EventId == activeEventId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(n => n.Status == status);
        }

        var list = await query.OrderByDescending(n => n.CreatedAtUtc).ToListAsync(ct);
        return list.Select(ToNomineeDto).ToList();
    }

    [HttpPost("admin/nominees/{id}/approve")]
    public async Task<ActionResult<NomineeDto>> Approve([FromRoute] string id, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var nominee = await FindNomineeAsync(activeEventId, id, ct);
        if (nominee is null) return NotFound();
        if (string.IsNullOrWhiteSpace(nominee.CategoryId)) return BadRequest("Nominee must have a category before approval.");

        nominee.Status = "approved";
        await _db.SaveChangesAsync(ct);
        return ToNomineeDto(nominee);
    }

    [HttpPost("admin/nominees/{id}/reject")]
    public async Task<ActionResult<NomineeDto>> Reject([FromRoute] string id, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var nominee = await FindNomineeAsync(activeEventId, id, ct);
        if (nominee is null) return NotFound();

        nominee.Status = "rejected";
        await _db.SaveChangesAsync(ct);
        return ToNomineeDto(nominee);
    }

    [HttpPost("admin/state")]
    public async Task<ActionResult<CanhoesEventStateDto>> UpdateState([FromBody] CanhoesEventStateDto dto, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var state = await EventModuleAccessEvaluator.GetOrCreateEventStateAsync(_db, activeEventId, ct);
        state.Phase = dto.Phase;
        state.NominationsVisible = dto.NominationsVisible;
        state.ResultsVisible = dto.ResultsVisible;
        await _db.SaveChangesAsync(ct);

        EventContextBootstrap.SyncLegacyPhaseState(_db, activeEventId, state.Phase, state.ResultsVisible);
        return new CanhoesEventStateDto(state.Phase, state.NominationsVisible, state.ResultsVisible);
    }
}
