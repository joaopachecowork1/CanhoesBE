using Canhoes.Api.Access;
using Canhoes.Api.DTOs;
using Canhoes.Api.Media;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Canhoes.Api.Mappers.EventMappers;
using Canhoes.Api.Data;
using Canhoes.Api.Services;
using Canhoes.Api.Auth;

namespace Canhoes.Api.Controllers;

[ApiController]
public class MemberExperienceController : EventControllerBase
{
    private readonly IMemberService _memberService;

    public MemberExperienceController(
        IMemberService memberService,
        CanhoesDbContext db, 
        IWebHostEnvironment? env = null) 
        : base(db, env: env)
    {
        _memberService = memberService;
    }
    [HttpGet("{eventId}/members")]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "eventId" })]
    public async Task<ActionResult<List<PublicUserDto>>> GetMembers([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Wishlist, ct);
        if (accessError is not null) return accessError;

        return Ok(await _memberService.GetMembersAsync(eventId, ct));
    }

    [HttpGet("{eventId}/measures")]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "eventId" })]
    public async Task<ActionResult<List<GalaMeasureDto>>> GetMeasures([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Measures, ct);
        if (accessError is not null) return accessError;

        return Ok(await _memberService.GetMeasuresAsync(eventId, ct));
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

        return Ok(await _memberService.CreateMeasureProposalAsync(eventId, userAccess.UserId, request, ct));
    }

    [HttpGet("{eventId}/results")]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client, VaryByQueryKeys = new[] { "eventId" })]
    public async Task<ActionResult<List<CanhoesCategoryResultDto>>> GetResults([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Gala, ct);
        if (accessError is not null) return accessError;

        return Ok(await _memberService.GetResultsAsync(eventId, ct));
    }

    [HttpGet("{eventId}/nominations/my-status")]
    public async Task<ActionResult<MyNominationStatusDto>> GetMyNominationStatus([FromRoute] string eventId, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Nominees, ct);
        if (accessError is not null) return accessError;

        return Ok(await _memberService.GetMyNominationStatusAsync(eventId, userAccess.UserId, ct));
    }

    [HttpGet("{eventId}/nominations/approved")]
    public async Task<ActionResult<List<NomineeDto>>> GetApprovedNominees([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, accessError) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Nominees, ct);
        if (accessError is not null) return accessError;

        return Ok(await _memberService.GetApprovedNomineesAsync(eventId, ct));
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

        return Ok(await _memberService.CreateNominationAsync(eventId, userAccess.UserId, request, ct));
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

        // Minimal check before upload logic in service
        var imageUrl = await SaveSingleImageAsync(file, "canhoes", "nominees", ct);
        var nominee = await _memberService.UpdateNomineeImageAsync(eventId, nomineeId, HttpContext.GetUserId(), HttpContext.User.IsInRole("Admin"), imageUrl, ct);
        
        if (nominee is null) return NotFound();
        return Ok(nominee);
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

        var imageUrl = await SaveSingleImageAsync(file, "canhoes", "wishlist", ct);
        var item = await _memberService.UpdateWishlistImageAsync(eventId, itemId, HttpContext.GetUserId(), HttpContext.User.IsInRole("Admin"), imageUrl, ct);

        if (item is null) return NotFound();
        return Ok(item);
    }

    [HttpDelete("{eventId}/wishlist/{itemId}")]
    public async Task<ActionResult> DeleteWishlistItem([FromRoute] string eventId, [FromRoute] string itemId, CancellationToken ct)
    {
        var success = await _memberService.DeleteWishlistItemAsync(eventId, itemId, HttpContext.GetUserId(), HttpContext.User.IsInRole("Admin"), ct);
        if (!success) return NotFound();
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

