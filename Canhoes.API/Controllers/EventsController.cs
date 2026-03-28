using System.Text.Json;
using Canhoes.Api.Auth;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
[Authorize]
public sealed class EventsController : ControllerBase
{
    private readonly CanhoesDbContext _db;

    public EventsController(CanhoesDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<EventSummaryDto>>> ListEvents(CancellationToken ct)
    {
        var events = await _db.Events
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        return Ok(events.Select(ToEventSummaryDto).ToList());
    }

    [HttpGet("{eventId}")]
    public async Task<ActionResult<EventContextDto>> GetEventContext([FromRoute] string eventId, CancellationToken ct)
    {
        var eventEntity = await _db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eventId, ct);

        if (eventEntity is null) return NotFound();

        var members = await _db.EventMembers
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .ToListAsync(ct);

        var users = await _db.Users
            .AsNoTracking()
            .Where(x => members.Select(m => m.UserId).Contains(x.Id))
            .ToListAsync(ct);

        var userLookup = users.ToDictionary(x => x.Id);

        var userDtos = members
            .Where(x => userLookup.ContainsKey(x.UserId))
            .OrderByDescending(x => x.Role == EventRoles.Admin)
            .ThenBy(x => userLookup.TryGetValue(x.UserId, out var user) ? GetUserName(user) : x.UserId.ToString())
            .Select(x =>
            {
                var user = userLookup[x.UserId];
                return new EventUserDto(user.Id, GetUserName(user), x.Role);
            })
            .ToList();

        var phaseDtos = await _db.EventPhases
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.StartDateUtc)
            .Select(x => new EventPhaseDto(
                x.Id,
                x.Type,
                new DateTimeOffset(x.StartDateUtc, TimeSpan.Zero),
                new DateTimeOffset(x.EndDateUtc, TimeSpan.Zero),
                x.IsActive
            ))
            .ToListAsync(ct);

        var activePhase = phaseDtos.FirstOrDefault(x => x.IsActive);

        return Ok(new EventContextDto(
            ToEventSummaryDto(eventEntity),
            userDtos,
            phaseDtos,
            activePhase
        ));
    }

    [HttpGet("{eventId}/feed/posts")]
    public async Task<ActionResult<List<EventFeedPostDto>>> GetPosts([FromRoute] string eventId, CancellationToken ct)
    {
        if (!await EventExistsAsync(eventId, ct)) return NotFound();

        var posts = await _db.HubPosts
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var authorIds = posts.Select(x => x.AuthorUserId).Distinct().ToList();
        var authors = await _db.Users
            .AsNoTracking()
            .Where(x => authorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var dtos = posts.Select(x =>
        {
            var author = authors.TryGetValue(x.AuthorUserId, out var value) ? value : null;
            return new EventFeedPostDto(
                x.Id,
                x.EventId,
                x.AuthorUserId,
                author is null ? "Unknown" : GetUserName(author),
                x.Text,
                x.MediaUrl,
                new DateTimeOffset(x.CreatedAtUtc, TimeSpan.Zero)
            );
        }).ToList();

        return Ok(dtos);
    }

    [HttpPost("{eventId}/feed/posts")]
    public async Task<ActionResult<EventFeedPostDto>> CreatePost(
        [FromRoute] string eventId,
        [FromBody] CreateEventPostRequest request,
        CancellationToken ct)
    {
        if (!await EventExistsAsync(eventId, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest("Content is required.");

        var userId = HttpContext.GetUserId();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return Unauthorized();

        var mediaUrls = string.IsNullOrWhiteSpace(request.ImageUrl)
            ? []
            : new List<string> { request.ImageUrl.Trim() };

        var post = new HubPostEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            AuthorUserId = userId,
            Text = request.Content.Trim(),
            MediaUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
            MediaUrlsJson = JsonSerializer.Serialize(mediaUrls),
            CreatedAtUtc = DateTime.UtcNow,
            IsPinned = false
        };

        _db.HubPosts.Add(post);
        await _db.SaveChangesAsync(ct);

        return Ok(new EventFeedPostDto(
            post.Id,
            post.EventId,
            post.AuthorUserId,
            GetUserName(user),
            post.Text,
            post.MediaUrl,
            new DateTimeOffset(post.CreatedAtUtc, TimeSpan.Zero)
        ));
    }

    [HttpGet("{eventId}/categories")]
    public async Task<ActionResult<List<EventCategoryDto>>> GetCategories([FromRoute] string eventId, CancellationToken ct)
    {
        if (!await EventExistsAsync(eventId, ct)) return NotFound();

        var categories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        return Ok(categories.Select(ToEventCategoryDto).ToList());
    }

    [HttpPost("{eventId}/categories")]
    public async Task<ActionResult<EventCategoryDto>> CreateCategory(
        [FromRoute] string eventId,
        [FromBody] CreateEventCategoryRequest request,
        CancellationToken ct)
    {
        if (!HttpContext.IsAdmin()) return Forbid();
        if (!await EventExistsAsync(eventId, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required.");
        if (!Enum.TryParse<AwardCategoryKind>(request.Kind, true, out var kind))
            return BadRequest("Invalid kind.");

        var sortOrder = request.SortOrder
            ?? (await _db.AwardCategories.Where(x => x.EventId == eventId).MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;

        var category = new AwardCategoryEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            Name = request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Kind = kind,
            SortOrder = sortOrder,
            IsActive = true
        };

        _db.AwardCategories.Add(category);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventCategoryDto(category));
    }

    [HttpGet("{eventId}/voting")]
    public async Task<ActionResult<EventVotingBoardDto>> GetVotingBoard([FromRoute] string eventId, CancellationToken ct)
    {
        if (!await EventExistsAsync(eventId, ct)) return NotFound();

        var userId = HttpContext.GetUserId();
        var votingPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);

        var categories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        var categoryIds = categories.Select(x => x.Id).ToList();

        var nominees = await _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Status == "approved" && x.CategoryId != null && categoryIds.Contains(x.CategoryId))
            .OrderBy(x => x.Title)
            .ToListAsync(ct);

        var members = await _db.EventMembers
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .ToListAsync(ct);

        var memberIds = members.Select(x => x.UserId).ToList();
        var users = await _db.Users
            .AsNoTracking()
            .Where(x => memberIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var nomineeVotes = await _db.Votes
            .AsNoTracking()
            .Where(x => x.UserId == userId && categoryIds.Contains(x.CategoryId))
            .ToDictionaryAsync(x => x.CategoryId, x => x.NomineeId, ct);

        var userVotes = await _db.UserVotes
            .AsNoTracking()
            .Where(x => x.VoterUserId == userId && categoryIds.Contains(x.CategoryId))
            .ToDictionaryAsync(x => x.CategoryId, x => x.TargetUserId.ToString(), ct);

        var categoryDtos = categories.Select(category =>
        {
            List<EventVoteOptionDto> options;
            string? myOptionId;

            if (category.Kind == AwardCategoryKind.UserVote)
            {
                options = members
                    .Where(x => users.ContainsKey(x.UserId))
                    .OrderByDescending(x => x.Role == EventRoles.Admin)
                    .ThenBy(x => GetUserName(users[x.UserId]))
                    .Select(x => new EventVoteOptionDto(
                        x.UserId.ToString(),
                        category.Id,
                        GetUserName(users[x.UserId])
                    ))
                    .ToList();

                myOptionId = userVotes.TryGetValue(category.Id, out var selectedUserId) ? selectedUserId : null;
            }
            else
            {
                options = nominees
                    .Where(x => x.CategoryId == category.Id)
                    .Select(x => new EventVoteOptionDto(x.Id, category.Id, x.Title))
                    .ToList();

                myOptionId = nomineeVotes.TryGetValue(category.Id, out var selectedNomineeId) ? selectedNomineeId : null;
            }

            return new EventVotingCategoryDto(
                category.Id,
                category.EventId,
                category.Name,
                category.Kind.ToString(),
                category.Description,
                category.VoteQuestion,
                options,
                myOptionId
            );
        }).ToList();

        return Ok(new EventVotingBoardDto(
            eventId,
            votingPhase?.Id,
            IsPhaseOpen(votingPhase),
            categoryDtos
        ));
    }

    [HttpPost("{eventId}/votes")]
    public async Task<ActionResult<EventVoteDto>> CastVote(
        [FromRoute] string eventId,
        [FromBody] CreateEventVoteRequest request,
        CancellationToken ct)
    {
        if (!await EventExistsAsync(eventId, ct)) return NotFound();

        var votingPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        if (!IsPhaseOpen(votingPhase)) return BadRequest("Voting is closed.");

        var category = await _db.AwardCategories
            .FirstOrDefaultAsync(x => x.Id == request.CategoryId && x.EventId == eventId && x.IsActive, ct);

        if (category is null) return BadRequest("Invalid category.");

        var userId = HttpContext.GetUserId();

        if (category.Kind == AwardCategoryKind.UserVote)
        {
            if (!Guid.TryParse(request.OptionId, out var targetUserId))
                return BadRequest("Invalid option.");

            var isMember = await _db.EventMembers
                .AsNoTracking()
                .AnyAsync(x => x.EventId == eventId && x.UserId == targetUserId, ct);

            if (!isMember) return BadRequest("Invalid option.");

            var existingUserVote = await _db.UserVotes
                .FirstOrDefaultAsync(x => x.CategoryId == category.Id && x.VoterUserId == userId, ct);

            if (existingUserVote is null)
            {
                existingUserVote = new UserVoteEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    CategoryId = category.Id,
                    VoterUserId = userId,
                    TargetUserId = targetUserId,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _db.UserVotes.Add(existingUserVote);
            }
            else
            {
                existingUserVote.TargetUserId = targetUserId;
                existingUserVote.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new EventVoteDto(
                existingUserVote.Id,
                existingUserVote.VoterUserId,
                existingUserVote.CategoryId,
                existingUserVote.TargetUserId.ToString(),
                votingPhase!.Id
            ));
        }

        var nominee = await _db.Nominees
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == request.OptionId &&
                x.EventId == eventId &&
                x.CategoryId == category.Id &&
                x.Status == "approved", ct);

        if (nominee is null) return BadRequest("Invalid option.");

        var existingVote = await _db.Votes
            .FirstOrDefaultAsync(x => x.CategoryId == category.Id && x.UserId == userId, ct);

        if (existingVote is null)
        {
            existingVote = new VoteEntity
            {
                Id = Guid.NewGuid().ToString(),
                CategoryId = category.Id,
                NomineeId = nominee.Id,
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.Votes.Add(existingVote);
        }
        else
        {
            existingVote.NomineeId = nominee.Id;
            existingVote.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new EventVoteDto(
            existingVote.Id,
            existingVote.UserId,
            existingVote.CategoryId,
            existingVote.NomineeId,
            votingPhase!.Id
        ));
    }

    [HttpGet("{eventId}/proposals")]
    public async Task<ActionResult<List<EventProposalDto>>> GetProposals([FromRoute] string eventId, CancellationToken ct)
    {
        if (!await EventExistsAsync(eventId, ct)) return NotFound();

        var proposals = await _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(proposals.Select(ToEventProposalDto).ToList());
    }

    [HttpPost("{eventId}/proposals")]
    public async Task<ActionResult<EventProposalDto>> CreateProposal(
        [FromRoute] string eventId,
        [FromBody] CreateEventProposalRequest request,
        CancellationToken ct)
    {
        if (!await EventExistsAsync(eventId, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest("Content is required.");

        var phase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Proposals, ct);
        if (!IsPhaseOpen(phase)) return BadRequest("Proposals are closed.");

        var proposal = new CategoryProposalEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            ProposedByUserId = HttpContext.GetUserId(),
            Name = request.Content.Trim(),
            Description = null,
            Status = "pending",
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.CategoryProposals.Add(proposal);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventProposalDto(proposal));
    }

    [HttpPatch("{eventId}/proposals/{proposalId}")]
    public async Task<ActionResult<EventProposalDto>> UpdateProposal(
        [FromRoute] string eventId,
        [FromRoute] string proposalId,
        [FromBody] UpdateEventProposalRequest request,
        CancellationToken ct)
    {
        if (!HttpContext.IsAdmin()) return Forbid();
        if (!await EventExistsAsync(eventId, ct)) return NotFound();

        var proposal = await _db.CategoryProposals
            .FirstOrDefaultAsync(x => x.Id == proposalId && x.EventId == eventId, ct);

        if (proposal is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Status)) return BadRequest("Status is required.");

        var status = request.Status.Trim().ToLowerInvariant();
        if (status is not ("pending" or "approved" or "rejected"))
            return BadRequest("Invalid status.");

        proposal.Status = status;

        if (status == "approved")
        {
            var categoryExists = await _db.AwardCategories
                .AsNoTracking()
                .AnyAsync(x => x.EventId == eventId && x.Name == proposal.Name, ct);

            if (!categoryExists)
            {
                var nextSortOrder = (await _db.AwardCategories
                    .Where(x => x.EventId == eventId)
                    .MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;

                _db.AwardCategories.Add(new AwardCategoryEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    EventId = eventId,
                    Name = proposal.Name,
                    Description = proposal.Description,
                    SortOrder = nextSortOrder,
                    Kind = AwardCategoryKind.Sticker,
                    IsActive = true
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        return Ok(ToEventProposalDto(proposal));
    }

    [HttpGet("{eventId}/wishlist")]
    public async Task<ActionResult<List<EventWishlistItemDto>>> GetWishlist([FromRoute] string eventId, CancellationToken ct)
    {
        if (!await EventExistsAsync(eventId, ct)) return NotFound();

        var items = await _db.WishlistItems
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(ct);

        return Ok(items.Select(x => new EventWishlistItemDto(
            x.Id,
            x.UserId,
            x.EventId,
            x.Title,
            x.Url,
            new DateTimeOffset(x.UpdatedAtUtc, TimeSpan.Zero)
        )).ToList());
    }

    [HttpPost("{eventId}/wishlist")]
    public async Task<ActionResult<EventWishlistItemDto>> CreateWishlistItem(
        [FromRoute] string eventId,
        [FromBody] CreateEventWishlistItemRequest request,
        CancellationToken ct)
    {
        if (!await EventExistsAsync(eventId, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required.");

        var item = new WishlistItemEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            UserId = HttpContext.GetUserId(),
            Title = request.Title.Trim(),
            Url = string.IsNullOrWhiteSpace(request.Link) ? null : request.Link.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.WishlistItems.Add(item);
        await _db.SaveChangesAsync(ct);

        return Ok(new EventWishlistItemDto(
            item.Id,
            item.UserId,
            item.EventId,
            item.Title,
            item.Url,
            new DateTimeOffset(item.UpdatedAtUtc, TimeSpan.Zero)
        ));
    }

    private Task<bool> EventExistsAsync(string eventId, CancellationToken ct) =>
        _db.Events.AsNoTracking().AnyAsync(x => x.Id == eventId, ct);

    private Task<EventPhaseEntity?> GetActivePhaseAsync(string eventId, string phaseType, CancellationToken ct) =>
        _db.EventPhases
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Type == phaseType && x.IsActive)
            .OrderByDescending(x => x.StartDateUtc)
            .FirstOrDefaultAsync(ct);

    private static bool IsPhaseOpen(EventPhaseEntity? phase)
    {
        if (phase is null || !phase.IsActive) return false;

        var now = DateTime.UtcNow;
        return phase.StartDateUtc <= now && now <= phase.EndDateUtc;
    }

    private static EventSummaryDto ToEventSummaryDto(EventEntity entity) =>
        new(entity.Id, entity.Name, entity.IsActive);

    private static EventCategoryDto ToEventCategoryDto(AwardCategoryEntity entity) =>
        new(entity.Id, entity.EventId, entity.Name, entity.Kind.ToString(), entity.IsActive, entity.Description);

    private static EventProposalDto ToEventProposalDto(CategoryProposalEntity entity) =>
        new(
            entity.Id,
            entity.EventId,
            entity.ProposedByUserId,
            BuildProposalContent(entity),
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero)
        );

    private static string BuildProposalContent(CategoryProposalEntity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Description)) return entity.Name;
        return $"{entity.Name}\n\n{entity.Description}";
    }

    private static string GetUserName(UserEntity user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName!;
}
