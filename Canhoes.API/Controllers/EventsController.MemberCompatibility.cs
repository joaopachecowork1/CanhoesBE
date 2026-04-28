using Canhoes.Api.Access;
using Canhoes.Api.DTOs;
using Canhoes.Api.Media;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Canhoes.Api.Controllers.EventsControllerMappers;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    [HttpGet("{eventId}/members")]
    public async Task<ActionResult<List<PublicUserDto>>> GetMembers([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Wishlist, ct);
        if (accessError is not null) return accessError;

        var memberDirectory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var members = memberDirectory.Members
            .Where(member => memberDirectory.UsersById.ContainsKey(member.UserId))
            .OrderByDescending(member => member.Role == EventRoles.Admin)
            .ThenBy(member => GetUserName(memberDirectory.UsersById[member.UserId]))
            .Select(member => ToPublicUserDto(
                memberDirectory.UsersById[member.UserId],
                member.Role == EventRoles.Admin))
            .ToList();

        return Ok(members);
    }

    [HttpGet("{eventId}/measures")]
    public async Task<ActionResult<List<GalaMeasureDto>>> GetMeasures([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Measures, ct);
        if (accessError is not null) return accessError;

        var measures = await _db.Measures
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.IsActive)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new GalaMeasureDto(
                x.Id,
                x.Text,
                x.IsActive,
                new DateTimeOffset(x.CreatedAtUtc, TimeSpan.Zero)))
            .ToListAsync(ct);

        return Ok(measures);
    }

    [HttpPost("{eventId}/measures/proposals")]
    public async Task<ActionResult<MeasureProposalDto>> CreateMeasureProposal(
        [FromRoute] string eventId,
        [FromBody] CreateMeasureProposalRequest request,
        CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Measures, ct);
        if (accessError is not null) return accessError;
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest("Text is required.");

        var activeProposalsPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Proposals, ct);
        if (!IsPhaseOpen(activeProposalsPhase)) return BadRequest("Proposals are closed.");

        var proposal = new MeasureProposalEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            ProposedByUserId = userAccess.UserId,
            Text = request.Text.Trim(),
            Status = ProposalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.MeasureProposals.Add(proposal);
        await _db.SaveChangesAsync(ct);

        return Ok(ToMeasureProposalDto(proposal));
    }

    [HttpGet("{eventId}/results")]
    public async Task<ActionResult<List<CanhoesCategoryResultDto>>> GetResults([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Gala, ct);
        if (accessError is not null) return accessError;

        var categories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        if (categories.Count == 0) return Ok(new List<CanhoesCategoryResultDto>());

        var categoryIds = categories.Select(x => x.Id).ToList();
        var nominees = await _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.CategoryId != null && x.Status == ProposalStatus.Approved)
            .ToListAsync(ct);

        var nomineeIds = nominees.Select(x => x.Id).ToList();
        var nomineeVotes = nomineeIds.Count == 0
            ? new List<VoteEntity>()
            : await _db.Votes
                .AsNoTracking()
                .Where(x => nomineeIds.Contains(x.NomineeId))
                .ToListAsync(ct);

        var userVotes = await _db.UserVotes
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.CategoryId))
            .ToListAsync(ct);

        var memberDirectory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var nomineesByCategoryId = nominees
            .GroupBy(x => x.CategoryId!)
            .ToDictionary(group => group.Key, group => group.ToList());

        var results = categories.Select(category =>
        {
            if (category.Kind == AwardCategoryKind.UserVote)
            {
                var topUsers = userVotes
                    .Where(vote => vote.CategoryId == category.Id)
                    .GroupBy(vote => vote.TargetUserId)
                    .Select(group =>
                    {
                        var userName = memberDirectory.UsersById.TryGetValue(group.Key, out var user)
                            ? GetUserName(user)
                            : group.Key.ToString();

                        return new CanhoesResultNomineeDto(
                            group.Key.ToString(),
                            category.Id,
                            userName,
                            null,
                            group.Count());
                    })
                    .OrderByDescending(x => x.Votes)
                    .ThenBy(x => x.Title)
                    .Take(3)
                    .ToList();

                return new CanhoesCategoryResultDto(
                    category.Id,
                    category.Name,
                    userVotes.Count(vote => vote.CategoryId == category.Id),
                    topUsers);
            }

            var topNominees = (nomineesByCategoryId.TryGetValue(category.Id, out var categoryNominees)
                    ? categoryNominees
                    : [])
                .Select(nominee => new CanhoesResultNomineeDto(
                    nominee.Id,
                    nominee.CategoryId,
                    nominee.Title,
                    MediaUrlFormatter.Normalize(nominee.ImageUrl),
                    nomineeVotes.Count(vote => vote.NomineeId == nominee.Id)))
                .OrderByDescending(x => x.Votes)
                .ThenBy(x => x.Title)
                .Take(3)
                .ToList();

            return new CanhoesCategoryResultDto(
                category.Id,
                category.Name,
                nomineeVotes.Count(vote => vote.CategoryId == category.Id),
                topNominees);
        }).ToList();

        return Ok(results);
    }

    [HttpGet("{eventId}/nominations/my-status")]
    public async Task<ActionResult<MyNominationStatusDto>> GetMyNominationStatus([FromRoute] string eventId, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Nominees, ct);
        if (accessError is not null) return accessError;

        var latestNomination = await _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.SubmittedByUserId == userAccess.UserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (latestNomination is null)
        {
            return Ok(new MyNominationStatusDto(false, null, null, null, null));
        }

        return Ok(new MyNominationStatusDto(
            true,
            latestNomination.CategoryId,
            latestNomination.Status,
            latestNomination.CategoryId,
            latestNomination.Title));
    }

    [HttpGet("{eventId}/nominations/approved")]
    public async Task<ActionResult<List<NomineeDto>>> GetApprovedNominees([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Nominees, ct);
        if (accessError is not null) return accessError;

        var nominees = await _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Status == ProposalStatus.Approved)
            .OrderBy(x => x.Title)
            .ToListAsync(ct);

        return Ok(nominees.Select(MapNomineeDto).ToList());
    }

    [HttpPost("{eventId}/nominations")]
    public async Task<ActionResult<NomineeDto>> CreateNomination(
        [FromRoute] string eventId,
        [FromBody] CreateNomineeRequest request,
        CancellationToken ct)
    {
        var requestedKind = NormalizeSubmissionKind(request.Kind);
        var moduleKey = requestedKind == "stickers" ? EventModuleKey.Stickers : EventModuleKey.Nominees;
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, moduleKey, ct);
        if (accessError is not null) return accessError;
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required.");

        var activeProposalsPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Proposals, ct);
        if (!IsPhaseOpen(activeProposalsPhase)) return BadRequest("Nominations are closed.");

        if (!string.IsNullOrWhiteSpace(request.CategoryId))
        {
            var categoryExists = await _db.AwardCategories
                .AsNoTracking()
                .AnyAsync(x => x.Id == request.CategoryId && x.EventId == eventId, ct);
            if (!categoryExists) return BadRequest("Invalid category.");

            var hasExistingNominationForCategory = await _db.Nominees
                .AsNoTracking()
                .AnyAsync(x =>
                    x.EventId == eventId
                    && x.SubmittedByUserId == userAccess.UserId
                    && x.CategoryId == request.CategoryId
                    && x.Status != ProposalStatus.Rejected,
                    ct);
            if (hasExistingNominationForCategory)
            {
                return BadRequest("You already have a nomination for this category.");
            }
        }

        var nominee = new NomineeEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            CategoryId = string.IsNullOrWhiteSpace(request.CategoryId) ? null : request.CategoryId.Trim(),
            Title = request.Title.Trim(),
            SubmissionKind = requestedKind,
            SubmittedByUserId = userAccess.UserId,
            Status = ProposalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Nominees.Add(nominee);
        await _db.SaveChangesAsync(ct);

        return Ok(MapNomineeDto(nominee));
    }

    [HttpPost("{eventId}/nominations/{nomineeId}/upload")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<NomineeDto>> UploadNomineeImage(
        [FromRoute] string eventId,
        [FromRoute] string nomineeId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length <= 0) return BadRequest("File is required.");

        var nominee = await _db.Nominees
            .FirstOrDefaultAsync(x => x.Id == nomineeId && x.EventId == eventId, ct);
        if (nominee is null) return NotFound();

        var requestedModule = nominee.SubmissionKind == "stickers" ? EventModuleKey.Stickers : EventModuleKey.Nominees;
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, requestedModule, ct);
        if (accessError is not null) return accessError;
        if (nominee.SubmittedByUserId != userAccess.UserId && !userAccess.IsAdmin) return Forbid();

        nominee.ImageUrl = await SaveSingleImageAsync(file, "canhoes", "nominees", ct);
        await _db.SaveChangesAsync(ct);

        return Ok(MapNomineeDto(nominee));
    }

    [HttpPost("{eventId}/wishlist/{itemId}/image")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<EventWishlistItemDto>> UploadWishlistImage(
        [FromRoute] string eventId,
        [FromRoute] string itemId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length <= 0) return BadRequest("File is required.");

        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Wishlist, ct);
        if (accessError is not null) return accessError;

        var wishlistItem = await _db.WishlistItems
            .FirstOrDefaultAsync(x => x.Id == itemId && x.EventId == eventId, ct);
        if (wishlistItem is null) return NotFound();
        if (wishlistItem.UserId != userAccess.UserId && !userAccess.IsAdmin) return Forbid();

        wishlistItem.ImageUrl = await SaveSingleImageAsync(file, "canhoes", "wishlist", ct);
        wishlistItem.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventWishlistItemDto(wishlistItem));
    }

    [HttpDelete("{eventId}/wishlist/{itemId}")]
    public async Task<ActionResult> DeleteWishlistItem([FromRoute] string eventId, [FromRoute] string itemId, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Wishlist, ct);
        if (accessError is not null) return accessError;

        var wishlistItem = await _db.WishlistItems
            .FirstOrDefaultAsync(x => x.Id == itemId && x.EventId == eventId, ct);
        if (wishlistItem is null) return NotFound();
        if (wishlistItem.UserId != userAccess.UserId && !userAccess.IsAdmin) return Forbid();

        _db.WishlistItems.Remove(wishlistItem);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string NormalizeSubmissionKind(string? kind)
    {
        var normalized = kind?.Trim().ToLowerInvariant();
        return normalized == "stickers" ? "stickers" : "nominees";
    }

    private NomineeDto MapNomineeDto(NomineeEntity entity) =>
        ToNomineeDto(entity) with
        {
            ImageUrl = MediaUrlFormatter.Normalize(entity.ImageUrl)
        };

    private async Task<string> SaveSingleImageAsync(
        IFormFile file,
        string firstDirectory,
        string secondDirectory,
        CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var normalizedExtension = extension.ToLowerInvariant();
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".webp"
        };

        if (!allowedExtensions.Contains(normalizedExtension))
        {
            throw new InvalidOperationException("Unsupported file type.");
        }

        var webRoot = _env?.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var directory = Path.Combine(webRoot, "uploads", firstDirectory, secondDirectory);
        Directory.CreateDirectory(directory);

        var fileName = $"{Guid.NewGuid():N}{normalizedExtension}";
        var filePath = Path.Combine(directory, fileName);
        await using var input = file.OpenReadStream();
        await using var output = new FileStream(filePath, FileMode.Create);
        await input.CopyToAsync(output, ct);

        return $"/uploads/{firstDirectory}/{secondDirectory}/{fileName}";
    }
}

public record MyNominationStatusDto(
    bool HasNomination,
    string? CategoryId,
    string? Status,
    string? NomineeId,
    string? NomineeTitle);
