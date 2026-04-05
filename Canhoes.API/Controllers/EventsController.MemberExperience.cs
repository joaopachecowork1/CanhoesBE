using System.Text.Json;
using Canhoes.Api.Access;
using Canhoes.Api.Media;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    [HttpGet("{eventId}/voting/overview")]
    public async Task<ActionResult<EventVotingOverviewDto>> GetVotingOverview([FromRoute] string eventId, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (error is not null) return error;

        var votingPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        var activeCategories = await LoadActiveCategoriesAsync(eventId, ct);
        var submittedVoteCount = await CountSubmittedVotesAsync(
            access.UserId,
            activeCategories.Select(x => x.Id).ToList(),
            ct);

        return Ok(new EventVotingOverviewDto(
            eventId,
            votingPhase?.Id,
            IsPhaseOpen(votingPhase),
            votingPhase is null ? null : new DateTimeOffset(votingPhase.EndDateUtc, TimeSpan.Zero),
            activeCategories.Count,
            submittedVoteCount,
            Math.Max(0, activeCategories.Count - submittedVoteCount)
        ));
    }

    [HttpGet("{eventId}/secret-santa/overview")]
    public async Task<ActionResult<EventSecretSantaOverviewDto>> GetSecretSantaOverview([FromRoute] string eventId, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.SecretSanta,
            ct);
        if (error is not null) return error;

        var myWishlistItemCount = await CountWishlistItemsAsync(eventId, access.UserId, ct);
        var latestDraw = await GetLatestSecretSantaDrawAsync(eventId, ct);
        if (latestDraw is null)
        {
            return Ok(new EventSecretSantaOverviewDto(
                eventId,
                false,
                false,
                null,
                null,
                0,
                myWishlistItemCount
            ));
        }

        var assignment = await _db.SecretSantaAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DrawId == latestDraw.Id && x.GiverUserId == access.UserId, ct);

        EventUserDto? assignedUser = null;
        var assignedWishlistItemCount = 0;

        if (assignment is not null)
        {
            var receiver = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == assignment.ReceiverUserId, ct);

            if (receiver is not null)
            {
                var role = await _db.EventMembers
                    .AsNoTracking()
                    .Where(x => x.EventId == eventId && x.UserId == receiver.Id)
                    .Select(x => x.Role)
                    .FirstOrDefaultAsync(ct)
                    ?? EventRoles.User;

                assignedUser = new EventUserDto(receiver.Id, GetUserName(receiver), role);
                assignedWishlistItemCount = await CountWishlistItemsAsync(eventId, receiver.Id, ct);
            }
        }

        return Ok(new EventSecretSantaOverviewDto(
            eventId,
            true,
            assignment is not null && assignedUser is not null,
            latestDraw.EventCode,
            assignedUser,
            assignedWishlistItemCount,
            myWishlistItemCount
        ));
    }

    [HttpGet("{eventId}/feed/posts")]
    public async Task<ActionResult<List<EventFeedPostDto>>> GetPosts([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;

        var posts = await _db.HubPosts
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        var postIds = posts.Select(post => post.Id).ToList();

        var mediaByPostId = await _db.HubPostMedia
            .AsNoTracking()
            .Where(x => x.PostId != null && postIds.Contains(x.PostId))
            .OrderBy(x => x.UploadedAtUtc)
            .ToListAsync(ct);

        var authorIds = posts.Select(x => x.AuthorUserId).Distinct().ToList();
        var authors = await _db.Users
            .AsNoTracking()
            .Where(x => authorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var dtos = posts.Select(x =>
        {
            var author = authors.TryGetValue(x.AuthorUserId, out var value) ? value : null;
            var mediaUrls = MediaUrlFormatter.Collect(
                x.MediaUrl,
                x.MediaUrlsJson,
                mediaByPostId
                    .Where(media => media.PostId == x.Id)
                    .Select(media => media.Url)
            );

            return ToEventFeedPostDto(x, author is null ? "Unknown" : GetUserName(author), mediaUrls);
        }).ToList();

        return Ok(dtos);
    }

    [HttpPost("{eventId}/feed/posts")]
    public async Task<ActionResult<EventFeedPostDto>> CreatePost([FromRoute] string eventId, [FromBody] CreateEventPostRequest request, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(eventId, EventModuleKey.Feed, ct);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest("Content is required.");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == access.UserId, ct);
        if (user is null) return Unauthorized();

        var mediaUrls = MediaUrlFormatter.Collect(request.ImageUrl, null);

        var post = new HubPostEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            AuthorUserId = access.UserId,
            Text = request.Content.Trim(),
            MediaUrl = mediaUrls.FirstOrDefault(),
            MediaUrlsJson = JsonSerializer.Serialize(mediaUrls),
            CreatedAtUtc = DateTime.UtcNow,
            IsPinned = false
        };

        _db.HubPosts.Add(post);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventFeedPostDto(post, GetUserName(user), mediaUrls));
    }

    [HttpGet("{eventId}/categories")]
    public async Task<ActionResult<List<EventCategoryDto>>> GetCategories([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (error is not null) return error;

        var categories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        return Ok(categories.Select(ToEventCategoryDto).ToList());
    }

    [HttpPost("{eventId}/categories")]
    public async Task<ActionResult<EventCategoryDto>> CreateCategory([FromRoute] string eventId, [FromBody] CreateEventCategoryRequest request, CancellationToken ct)
    {
        var (_, error) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required.");
        if (!Enum.TryParse<AwardCategoryKind>(request.Kind, true, out var kind))
            return BadRequest("Invalid kind.");

        var category = await CreateCategoryEntityAsync(
            eventId,
            request.Title,
            request.Description,
            request.SortOrder,
            kind,
            ct);

        return Ok(ToEventCategoryDto(category));
    }

    [HttpGet("{eventId}/voting")]
    public async Task<ActionResult<EventVotingBoardDto>> GetVotingBoard([FromRoute] string eventId, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (error is not null) return error;

        var votingPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);

        var categories = await LoadActiveCategoriesAsync(eventId, ct);
        var categoryIds = categories.Select(x => x.Id).ToList();

        var nominees = await _db.Nominees
            .AsNoTracking()
            .Where(x =>
                x.EventId == eventId
                && x.Status == "approved"
                && x.CategoryId != null
                && categoryIds.Contains(x.CategoryId))
            .OrderBy(x => x.Title)
            .ToListAsync(ct);

        var directory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var members = directory.Members;
        var users = directory.UsersById;

        var nomineeVotes = await _db.Votes
            .AsNoTracking()
            .Where(x => x.UserId == access.UserId && categoryIds.Contains(x.CategoryId))
            .ToDictionaryAsync(x => x.CategoryId, x => x.NomineeId, ct);

        var userVotes = await _db.UserVotes
            .AsNoTracking()
            .Where(x => x.VoterUserId == access.UserId && categoryIds.Contains(x.CategoryId))
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

                myOptionId = userVotes.TryGetValue(category.Id, out var selectedUserId)
                    ? selectedUserId
                    : null;
            }
            else
            {
                options = nominees
                    .Where(x => x.CategoryId == category.Id)
                    .Select(x => new EventVoteOptionDto(x.Id, category.Id, x.Title))
                    .ToList();

                myOptionId = nomineeVotes.TryGetValue(category.Id, out var selectedNomineeId)
                    ? selectedNomineeId
                    : null;
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
    public async Task<ActionResult<EventVoteDto>> CastVote([FromRoute] string eventId, [FromBody] CreateEventVoteRequest request, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Voting,
            ct);
        if (error is not null) return error;

        var votingPhase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        if (!IsPhaseOpen(votingPhase)) return BadRequest("Voting is closed.");

        var category = await _db.AwardCategories
            .FirstOrDefaultAsync(
                x => x.Id == request.CategoryId && x.EventId == eventId && x.IsActive,
                ct);

        if (category is null) return BadRequest("Invalid category.");

        if (category.Kind == AwardCategoryKind.UserVote)
        {
            if (!Guid.TryParse(request.OptionId, out var targetUserId))
                return BadRequest("Invalid option.");

            var isMember = await _db.EventMembers
                .AsNoTracking()
                .AnyAsync(x => x.EventId == eventId && x.UserId == targetUserId, ct);

            if (!isMember) return BadRequest("Invalid option.");

            var existingUserVote = await _db.UserVotes
                .FirstOrDefaultAsync(
                    x => x.CategoryId == category.Id && x.VoterUserId == access.UserId,
                    ct);

            if (existingUserVote is null)
            {
                existingUserVote = new UserVoteEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    CategoryId = category.Id,
                    VoterUserId = access.UserId,
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
                x.Id == request.OptionId
                && x.EventId == eventId
                && x.CategoryId == category.Id
                && x.Status == "approved", ct);

        if (nominee is null) return BadRequest("Invalid option.");

        var existingVote = await _db.Votes
            .FirstOrDefaultAsync(x => x.CategoryId == category.Id && x.UserId == access.UserId, ct);

        if (existingVote is null)
        {
            existingVote = new VoteEntity
            {
                Id = Guid.NewGuid().ToString(),
                CategoryId = category.Id,
                NomineeId = nominee.Id,
                UserId = access.UserId,
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
        var (_, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (error is not null) return error;

        var proposals = await _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(proposals.Select(ToEventProposalDto).ToList());
    }

    [HttpPost("{eventId}/proposals")]
    public async Task<ActionResult<EventProposalDto>> CreateProposal([FromRoute] string eventId, [FromBody] CreateEventProposalRequest request, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Categories,
            ct);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        var phase = await GetActivePhaseAsync(eventId, EventPhaseTypes.Proposals, ct);
        if (!IsPhaseOpen(phase)) return BadRequest("Proposals are closed.");

        var proposal = new CategoryProposalEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            ProposedByUserId = access.UserId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim(),
            Status = "pending",
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.CategoryProposals.Add(proposal);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventProposalDto(proposal));
    }

    [HttpPatch("{eventId}/proposals/{proposalId}")]
    public async Task<ActionResult<EventProposalDto>> UpdateProposal([FromRoute] string eventId, [FromRoute] string proposalId, [FromBody] UpdateEventProposalRequest request, CancellationToken ct)
    {
        var (_, error) = await RequireEventAccessAsync(eventId, ct, requireManage: true);
        if (error is not null) return error;

        var proposal = await _db.CategoryProposals
            .FirstOrDefaultAsync(x => x.Id == proposalId && x.EventId == eventId, ct);

        if (proposal is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Status)) return BadRequest("Status is required.");

        var status = NormalizeProposalStatus(request.Status);
        if (status is null)
            return BadRequest("Invalid status.");

        await ApplyCategoryProposalStatusAsync(proposal, eventId, status, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventProposalDto(proposal));
    }

    [HttpGet("{eventId}/wishlist")]
    public async Task<ActionResult<List<EventWishlistItemDto>>> GetWishlist([FromRoute] string eventId, CancellationToken ct)
    {
        var (_, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Wishlist,
            ct);
        if (error is not null) return error;

        var items = await _db.WishlistItems
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(ct);

        return Ok(items.Select(ToEventWishlistItemDto).ToList());
    }

    [HttpPost("{eventId}/wishlist")]
    public async Task<ActionResult<EventWishlistItemDto>> CreateWishlistItem([FromRoute] string eventId, [FromBody] CreateEventWishlistItemRequest request, CancellationToken ct)
    {
        var (access, _, error) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.Wishlist,
            ct);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required.");

        var item = new WishlistItemEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            UserId = access.UserId,
            Title = request.Title.Trim(),
            Url = string.IsNullOrWhiteSpace(request.Link) ? null : request.Link.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.WishlistItems.Add(item);
        await _db.SaveChangesAsync(ct);

        return Ok(ToEventWishlistItemDto(item));
    }
}
