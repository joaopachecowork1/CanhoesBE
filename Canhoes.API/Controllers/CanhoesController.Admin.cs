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

        // OPTIMIZATION: Load both tables in parallel (was sequential)
        var catProposalsTask = _db.CategoryProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        var measureProposalsTask = _db.MeasureProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        await Task.WhenAll(catProposalsTask, measureProposalsTask);

        var allCatProposals = await catProposalsTask;
        var allMeasureProposals = await measureProposalsTask;

        var catsPending = allCatProposals.Where(p => p.Status == ProposalStatus.Pending).Select(ToCategoryProposalDto);
        var catsApproved = allCatProposals.Where(p => p.Status == ProposalStatus.Approved).Select(ToCategoryProposalDto);
        var catsRejected = allCatProposals.Where(p => p.Status == ProposalStatus.Rejected).Select(ToCategoryProposalDto);

        var measPending = allMeasureProposals.Where(p => p.Status == ProposalStatus.Pending).Select(ToMeasureProposalDto);
        var measApproved = allMeasureProposals.Where(p => p.Status == ProposalStatus.Approved).Select(ToMeasureProposalDto);
        var measRejected = allMeasureProposals.Where(p => p.Status == ProposalStatus.Rejected).Select(ToMeasureProposalDto);

        var dto = new AdminProposalsHistoryDto(
            new ProposalsByStatus<CategoryProposalDto>(catsPending, catsApproved, catsRejected),
            new ProposalsByStatus<MeasureProposalDto>(measPending, measApproved, measRejected));

        return Ok(dto);
    }

    [HttpGet("admin/categories")]
    public async Task<ActionResult<PagedResult<AwardCategoryDto>>> AdminGetCategories(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var total = await _db.AwardCategories
            .CountAsync(x => x.EventId == activeEventId, ct);

        var cats = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == activeEventId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var dtos = cats.Select(ToAwardCategoryDto).ToList();
        return new PagedResult<AwardCategoryDto>(dtos, total, skip, take, (skip + take) < total);
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

        // OPTIMIZATION: Load all three tables in parallel (was sequential)
        var nomineesTask = _db.Nominees.AsNoTracking()
            .Where(n => n.EventId == activeEventId && n.Status == ProposalStatus.Pending)
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToListAsync(ct);

        var catProposalsTask = _db.CategoryProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId && p.Status == ProposalStatus.Pending)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        var measureProposalsTask = _db.MeasureProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId && p.Status == ProposalStatus.Pending)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        await Task.WhenAll(nomineesTask, catProposalsTask, measureProposalsTask);

        var nominees = await nomineesTask;
        var cats = await catProposalsTask;
        var meas = await measureProposalsTask;

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

        proposal.Status = ProposalStatus.Approved;
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

        proposal.Status = ProposalStatus.Rejected;
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

        proposal.Status = ProposalStatus.Approved;
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
    public async Task<ActionResult<PagedResult<MeasureProposalDto>>> AdminListMeasureProposals(
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var normalized = string.IsNullOrWhiteSpace(status)
            ? "all"
            : status.Trim().ToLowerInvariant();

        IQueryable<MeasureProposalEntity> query = _db.MeasureProposals.AsNoTracking()
            .Where(p => p.EventId == activeEventId);
        if (ProposalStatus.IsValid(normalized))
        {
            query = query.Where(p => p.Status == normalized);
        }

        var total = await query.CountAsync(ct);
        var list = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var dtos = list.Select(ToMeasureProposalDto).ToList();
        return new PagedResult<MeasureProposalDto>(dtos, total, skip, take, (skip + take) < total);
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
            if (!ProposalStatus.IsValid(statusValue))
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

        proposal.Status = ProposalStatus.Rejected;
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
    public async Task<ActionResult<PagedResult<NomineeDto>>> AdminGetAllNominees(
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var query = _db.Nominees.AsNoTracking().Where(n => n.EventId == activeEventId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(n => n.Status == status);
        }

        var total = await query.CountAsync(ct);
        var list = await query.OrderByDescending(n => n.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var dtos = list.Select(ToNomineeDto).ToList();
        return new PagedResult<NomineeDto>(dtos, total, skip, take, (skip + take) < total);
    }

    [HttpPost("admin/nominees/{id}/approve")]
    public async Task<ActionResult<NomineeDto>> Approve([FromRoute] string id, CancellationToken ct)
    {
        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var nominee = await FindNomineeAsync(activeEventId, id, ct);
        if (nominee is null) return NotFound();
        if (string.IsNullOrWhiteSpace(nominee.CategoryId)) return BadRequest("Nominee must have a category before approval.");

        nominee.Status = ProposalStatus.Approved;
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

        nominee.Status = ProposalStatus.Rejected;
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
