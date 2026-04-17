using Canhoes.Api.Access;
using Canhoes.Api.Caching;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public partial class CanhoesController
{
    [HttpGet("state")]
    [OutputCache(PolicyName = "EventState", VaryByQueryKeys = new[] { "*" })]
    public async Task<ActionResult<CanhoesEventStateDto>> GetState(CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct);
        if (error is not null) return error;

        var state = access.ModuleAccess.State;
        return new CanhoesEventStateDto(state.Phase, state.NominationsVisible, state.ResultsVisible);
    }

    [HttpGet("categories")]
    [OutputCache(PolicyName = "Categories", VaryByQueryKeys = new[] { "*" })]
    public async Task<ActionResult<PagedResult<AwardCategoryDto>>> GetCategories(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        // For default page size, use cache; for custom pagination, skip cache
        if (skip == 0 && take == 50)
        {
            var cacheKey = CacheKeys.ForEvent(CacheKeys.Categories, access.EventId);
            var allCategories = await _cache.GetOrCreateAsync(
                cacheKey,
                CacheKeys.CategoriesTtl,
                async (token) =>
                {
                    var cats = await _db.AwardCategories.AsNoTracking()
                        .Where(c => c.EventId == access.EventId && c.IsActive)
                        .OrderBy(c => c.SortOrder)
                        .ToListAsync(token);
                    return cats.Select(ToAwardCategoryDto).ToList();
                },
                ct);

            var total = allCategories.Count;
            var pagedItems = allCategories.Skip(skip).Take(take).ToList();
            return new PagedResult<AwardCategoryDto>(pagedItems, total, skip, take, (skip + take) < total);
        }

        var totalDirect = await _db.AwardCategories
            .CountAsync(c => c.EventId == access.EventId && c.IsActive, ct);

        var catsDirect = await _db.AwardCategories.AsNoTracking()
            .Where(c => c.EventId == access.EventId && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var dtosDirect = catsDirect.Select(ToAwardCategoryDto).ToList();
        return new PagedResult<AwardCategoryDto>(dtosDirect, totalDirect, skip, take, (skip + take) < totalDirect);
    }

    [HttpGet("nominees")]
    public async Task<ActionResult<PagedResult<NomineeDto>>> GetNominees(
        [FromQuery] string? categoryId,
        [FromQuery] string? kind,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var nomineeKind = NormalizeNomineeKind(kind);
        var (access, error) = await RequireActiveEventAccessAsync(
            ct,
            moduleKey: GetNomineeModuleKey(nomineeKind));
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var q = _db.Nominees.AsNoTracking().Where(n => n.EventId == access.EventId);
        if (!string.IsNullOrWhiteSpace(categoryId)) q = q.Where(n => n.CategoryId == categoryId);
        q = q.Where(n => n.SubmissionKind == nomineeKind);

        var state = access.ModuleAccess.State;
        if (!state.NominationsVisible) q = q.Where(n => n.Status == ProposalStatus.Approved);
        if (!access.IsAdmin) q = q.Where(n => n.Status != ProposalStatus.Rejected);

        var total = await q.CountAsync(ct);
        var list = await q.OrderByDescending(n => n.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var dtos = list.Select(ToNomineeDto).ToList();
        return new PagedResult<NomineeDto>(dtos, total, skip, take, (skip + take) < total);
    }

    [HttpGet("measures")]
    public async Task<ActionResult<PagedResult<GalaMeasureDto>>> GetMeasures(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Measures);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        // For default page size, use cache
        if (skip == 0 && take == 50)
        {
            var cacheKey = CacheKeys.ForEvent(CacheKeys.Measures, access.EventId);
            var allMeasures = await _cache.GetOrCreateAsync(
                cacheKey,
                CacheKeys.MeasuresTtl,
                async (token) =>
                {
                    var measures = await _db.Measures.AsNoTracking()
                        .Where(m => m.EventId == access.EventId && m.IsActive)
                        .OrderByDescending(m => m.CreatedAtUtc)
                        .ToListAsync(token);
                    return measures.Select(ToGalaMeasureDto).ToList();
                },
                ct);

            var total = allMeasures.Count;
            var pagedItems = allMeasures.Skip(skip).Take(take).ToList();
            return new PagedResult<GalaMeasureDto>(pagedItems, total, skip, take, (skip + take) < total);
        }

        var totalDirect = await _db.Measures.AsNoTracking()
            .CountAsync(m => m.EventId == access.EventId && m.IsActive, ct);

        var listDirect = await _db.Measures.AsNoTracking()
            .Where(m => m.EventId == access.EventId && m.IsActive)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var dtosDirect = listDirect.Select(ToGalaMeasureDto).ToList();
        return new PagedResult<GalaMeasureDto>(dtosDirect, totalDirect, skip, take, (skip + take) < totalDirect);
    }

    [HttpPost("nominees")]
    public async Task<ActionResult<NomineeDto>> CreateNominee([FromBody] CreateNomineeRequest req, CancellationToken ct)
    {
        var nomineeKind = NormalizeNomineeKind(req.Kind);
        var (access, error) = await RequireActiveEventAccessAsync(
            ct,
            moduleKey: GetNomineeModuleKey(nomineeKind));
        if (error is not null) return error;

        var state = access.ModuleAccess.State;
        if (state.Phase != LegacyPhaseNames.Nominations) return BadRequest("Nominations are closed.");

        var nominee = new NomineeEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = access.EventId,
            CategoryId = string.IsNullOrWhiteSpace(req.CategoryId) ? null : req.CategoryId,
            SubmissionKind = nomineeKind,
            Title = req.Title,
            SubmittedByUserId = access.UserId,
            Status = ProposalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Nominees.Add(nominee);
        await _db.SaveChangesAsync(ct);
        return ToNomineeDto(nominee);
    }

    [HttpPost("categories/proposals")]
    public async Task<ActionResult<CategoryProposalDto>> CreateCategoryProposal([FromBody] CreateCategoryProposalRequest req, CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Categories);
        if (error is not null) return error;

        var state = access.ModuleAccess.State;
        if (state.Phase != LegacyPhaseNames.Nominations) return BadRequest("Category proposals are closed.");

        var proposal = new CategoryProposalEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = access.EventId,
            Name = req.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            ProposedByUserId = access.UserId,
            Status = ProposalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.CategoryProposals.Add(proposal);
        await _db.SaveChangesAsync(ct);
        return ToCategoryProposalDto(proposal);
    }

    [HttpPost("measures/proposals")]
    public async Task<ActionResult<MeasureProposalDto>> CreateMeasureProposal([FromBody] CreateMeasureProposalRequest req, CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Measures);
        if (error is not null) return error;

        var state = access.ModuleAccess.State;
        if (state.Phase != LegacyPhaseNames.Nominations) return BadRequest("Measure proposals are closed.");

        var proposal = new MeasureProposalEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = access.EventId,
            Text = req.Text.Trim(),
            ProposedByUserId = access.UserId,
            Status = ProposalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.MeasureProposals.Add(proposal);
        await _db.SaveChangesAsync(ct);
        return ToMeasureProposalDto(proposal);
    }

    [HttpPost("nominees/{id}/upload")]
    [RequestSizeLimit(15_000_000)]
    public async Task<ActionResult<NomineeDto>> UploadNomineeImage([FromRoute] string id, IFormFile file, CancellationToken ct)
    {
        var nominee = await _db.Nominees.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (nominee is null) return NotFound();

        var (access, error) = await RequireActiveEventAccessAsync(
            ct,
            moduleKey: GetNomineeModuleKey(nominee.SubmissionKind));
        if (error is not null) return error;
        if (nominee.EventId != access.EventId) return NotFound();
        if (file.Length <= 0) return BadRequest("Empty file");

        var folder = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "canhoes");
        Directory.CreateDirectory(folder);

        var ext = Path.GetExtension(file.FileName);
        var safe = $"{id}{ext}";
        var full = Path.Combine(folder, safe);

        await using (var fs = System.IO.File.Create(full))
        {
            await file.CopyToAsync(fs, ct);
        }

        nominee.ImageUrl = $"/uploads/canhoes/{safe}";
        await _db.SaveChangesAsync(ct);

        return ToNomineeDto(nominee);
    }

    [HttpGet("my-votes")]
    public async Task<ActionResult<List<VoteDto>>> GetMyVotes(CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Voting);
        if (error is not null) return error;

        var categoryIds = await LoadEventCategoryIdsAsync(access.EventId, ct);
        var votes = await _db.Votes.AsNoTracking()
            .Where(v => v.UserId == access.UserId && categoryIds.Contains(v.CategoryId))
            .ToListAsync(ct);
        return votes.Select(ToVoteDto).ToList();
    }

    [HttpPost("vote")]
    public async Task<ActionResult<VoteDto>> CastVote([FromBody] CastVoteRequest req, CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Voting);
        if (error is not null) return error;

        var state = access.ModuleAccess.State;
        if (state.Phase != LegacyPhaseNames.Voting) return BadRequest("Voting is closed.");

        var nominee = await _db.Nominees.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == req.NomineeId && n.EventId == access.EventId && n.CategoryId == req.CategoryId, ct);
        if (nominee is null || nominee.Status != ProposalStatus.Approved) return BadRequest("Invalid nominee.");

        var existing = await _db.Votes.FirstOrDefaultAsync(v => v.CategoryId == req.CategoryId && v.UserId == access.UserId, ct);
        if (existing is null)
        {
            existing = new VoteEntity
            {
                Id = Guid.NewGuid().ToString(),
                CategoryId = req.CategoryId,
                NomineeId = req.NomineeId,
                UserId = access.UserId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.Votes.Add(existing);
        }
        else
        {
            existing.NomineeId = req.NomineeId;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return ToVoteDto(existing);
    }

    [HttpGet("results")]
    public async Task<ActionResult<List<CanhoesCategoryResultDto>>> GetResults(CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Gala);
        if (error is not null) return error;

        var state = access.ModuleAccess.State;
        if (!(state.ResultsVisible || state.Phase == LegacyPhaseNames.Gala || access.IsAdmin)) return Forbid();

        var eventId = access.EventId;

        // Sequential execution to avoid DbContext threading issues
        var categories = await _db.AwardCategories.AsNoTracking()
            .Where(c => c.EventId == eventId && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Id, c.Name, c.SortOrder })
            .ToListAsync(ct);

        var nominees = await _db.Nominees.AsNoTracking()
            .Where(n => n.EventId == eventId && n.Status == ProposalStatus.Approved && n.CategoryId != null)
            .Select(n => new { n.Id, n.CategoryId, n.Title, n.ImageUrl })
            .ToListAsync(ct);

        // OPTIMIZATION: Use GROUP BY to count votes per nominee in the database
        var voteCounts = await _db.Votes.AsNoTracking()
            .Where(v => _db.AwardCategories.Any(c => c.EventId == eventId && c.IsActive && c.Id == v.CategoryId))
            .GroupBy(v => new { v.CategoryId, v.NomineeId })
            .Select(g => new { g.Key.CategoryId, g.Key.NomineeId, Count = g.Count() })
            .ToListAsync(ct);

        // Build lookup dictionaries for O(1) access
        var voteCountByCategoryNominee = voteCounts
            .ToDictionary(vc => (vc.CategoryId, vc.NomineeId), vc => vc.Count);

        var result = new List<CanhoesCategoryResultDto>();
        foreach (var cat in categories)
        {
            var catNominees = nominees.Where(n => n.CategoryId == cat.Id).ToList();

            // Calculate total votes for this category
            var totalVotes = voteCounts.Where(vc => vc.CategoryId == cat.Id).Sum(vc => vc.Count);

            // Get top 3 nominees using database-computed counts
            var top = catNominees
                .Select(n => new CanhoesResultNomineeDto(
                    n.Id,
                    n.CategoryId,
                    n.Title,
                    n.ImageUrl,
                    voteCountByCategoryNominee.TryGetValue((cat.Id, n.Id), out var count) ? count : 0))
                .OrderByDescending(x => x.Votes)
                .ThenBy(x => x.Title)
                .Take(3)
                .ToList();

            result.Add(new CanhoesCategoryResultDto(cat.Id, cat.Name, totalVotes, top));
        }

        return result;
    }

    [HttpGet("my-user-votes")]
    public async Task<ActionResult<List<UserVoteDto>>> GetMyUserVotes(CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Voting);
        if (error is not null) return error;

        var categoryIds = await LoadEventCategoryIdsAsync(access.EventId, ct);
        var list = await _db.UserVotes.AsNoTracking()
            .Where(v => v.VoterUserId == access.UserId && categoryIds.Contains(v.CategoryId))
            .OrderByDescending(v => v.UpdatedAtUtc)
            .ToListAsync(ct);

        return list.Select(ToUserVoteDto).ToList();
    }

    [HttpPost("user-vote")]
    public async Task<ActionResult<UserVoteDto>> CastUserVote([FromBody] CastUserVoteRequest req, CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Voting);
        if (error is not null) return error;

        var state = access.ModuleAccess.State;
        if (state.Phase != LegacyPhaseNames.Voting) return BadRequest("Voting is closed.");

        var cat = await _db.AwardCategories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.CategoryId && c.EventId == access.EventId, ct);
        if (cat is null || cat.Kind != AwardCategoryKind.UserVote) return BadRequest("Invalid category.");

        var targetExists = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == req.TargetUserId, ct);
        if (!targetExists) return BadRequest("Invalid target user.");

        var existing = await _db.UserVotes.FirstOrDefaultAsync(v => v.CategoryId == req.CategoryId && v.VoterUserId == access.UserId, ct);
        if (existing is null)
        {
            existing = new UserVoteEntity
            {
                Id = Guid.NewGuid().ToString(),
                CategoryId = req.CategoryId,
                VoterUserId = access.UserId,
                TargetUserId = req.TargetUserId,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.UserVotes.Add(existing);
        }
        else
        {
            existing.TargetUserId = req.TargetUserId;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return ToUserVoteDto(existing);
    }
}
