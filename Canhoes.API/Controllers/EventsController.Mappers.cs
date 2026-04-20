using Canhoes.Api.Access;
using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;
using static Canhoes.Api.Controllers.EventsControllerMappers;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    private async Task<List<EventSummaryDto>> LoadEventSummariesAsync(CancellationToken ct)
    {
        return (await _db.Events
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToListAsync(ct))
            .Select(ToEventSummaryDto)
            .ToList();
    }

    private async Task<EventAdminStateDto> BuildAdminStateDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        var legacyState = await GetOrCreateEventStateAsync(eventId, ct);
        var phases = await LoadEventPhasesAsync(eventId, ct);
        var memberModuleAccess = await EventModuleAccessEvaluator.EvaluateAsync(
            _db,
            eventId,
            Guid.Empty,
            isAdmin: false,
            ct);

        var activePhaseEntity = memberModuleAccess.ActivePhase ?? phases.FirstOrDefault(x => x.IsActive);
        var activePhase = activePhaseEntity is null ? null : ToEventPhaseDto(activePhaseEntity);

        return new EventAdminStateDto(
            eventId,
            activePhase,
            phases.Select(ToEventPhaseDto).ToList(),
            legacyState.NominationsVisible,
            legacyState.ResultsVisible,
            memberModuleAccess.ModuleVisibility,
            memberModuleAccess.EffectiveModules,
            await BuildAdminCountsDtoAsync(eventId, ct)
        );
    }

    private async Task<EventAdminSecretSantaStateDto> BuildAdminSecretSantaStateDtoAsync(
        string eventId,
        string? requestedEventCode,
        CancellationToken ct)
    {
        var draw = string.IsNullOrWhiteSpace(requestedEventCode)
            ? await GetLatestSecretSantaDrawAsync(eventId, ct)
            : await _db.SecretSantaDraws
                .AsNoTracking()
                .Where(x => x.EventCode == requestedEventCode)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

        var eventCode = draw?.EventCode ?? NormalizeSecretSantaEventCode(eventId, requestedEventCode);
        var memberCount = await _db.EventMembers.AsNoTracking().CountAsync(x => x.EventId == eventId, ct);
        var assignmentCount = draw is null
            ? 0
            : await _db.SecretSantaAssignments.AsNoTracking().CountAsync(x => x.DrawId == draw.Id, ct);

        return new EventAdminSecretSantaStateDto(
            eventId,
            eventCode,
            draw is not null,
            draw?.Id,
            draw is null ? null : new DateTimeOffset(draw.CreatedAtUtc, TimeSpan.Zero),
            draw?.IsLocked ?? false,
            memberCount,
            assignmentCount
        );
    }

    private async Task<EventAdminBootstrapDto> BuildAdminBootstrapDtoAsync(
        string eventId,
        bool includeLists,
        CancellationToken ct)
    {
        _ = includeLists;
        var events = await LoadEventSummariesAsync(ct);
        var state = await BuildAdminStateDtoAsync(eventId, ct);
        var counts = await BuildAdminListCountsDtoAsync(eventId, ct);

        return new EventAdminBootstrapDto(
            events,
            state,
            counts
        );
    }

    private async Task<AdminListCountsDto> BuildAdminListCountsDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        // Sequential execution to avoid DbContext threading issues
        var categoryIds = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var categoryProposals = await _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Pending = g.Count(x => x.Status == ProposalStatus.Pending)
            })
            .FirstOrDefaultAsync(ct);

        var measureProposals = await _db.MeasureProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Pending = g.Count(x => x.Status == ProposalStatus.Pending)
            })
            .FirstOrDefaultAsync(ct);

        var nomineesCount = await _db.Nominees
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var membersCount = await _db.EventMembers
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var voteCategoriesCount = await _db.AwardCategories
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId && x.Kind == AwardCategoryKind.UserVote, ct);

        var votesTotal = categoryIds.Count == 0
            ? 0
            : await _db.Votes
                .AsNoTracking()
                .CountAsync(x => categoryIds.Contains(x.CategoryId), ct);

        return new AdminListCountsDto(
            nomineesCount,
            nomineesCount, // adminNomineesTotal = same source
            votesTotal,
            categoryProposals?.Total ?? 0,
            categoryProposals?.Pending ?? 0,
            measureProposals?.Total ?? 0,
            measureProposals?.Pending ?? 0,
            membersCount,
            voteCategoriesCount
        );
    }

    private async Task<EventCountsDto> BuildAdminCountsDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        // Reuse the consolidated single-query counts from LoadEventCountsAsync,
        // then fetch the category count separately (different filter).
        var counts = await LoadEventCountsAsync(eventId, ct);
        var categoryCount = await _db.AwardCategories
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        return new EventCountsDto(
            counts.MemberCount,
            counts.HubPostCount,
            categoryCount,
            counts.PendingCategoryProposalsCount,
            counts.WishlistCount
        );
    }

    private async Task<List<AwardCategoryDto>> LoadAdminCategoriesAsync(
        string eventId,
        CancellationToken ct)
    {
        var categories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        return categories.Select(ToAwardCategoryDto).ToList();
    }

    private async Task<AdminNomineeDto> BuildAdminNominationDtoAsync(
        NomineeEntity entity,
        CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == entity.SubmittedByUserId, ct);

        return ToAdminNomineeDto(
            entity,
            user is null ? entity.SubmittedByUserId.ToString() : GetUserName(user)
        );
    }

    // ---------- Paginated methods ----------

    private async Task<AdminVotesPagedDto> BuildAdminVotesPagedDtoAsync(
        string eventId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var categoryIds = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var total = categoryIds.Count == 0
            ? 0
            : await _db.Votes
                .AsNoTracking()
                .CountAsync(x => categoryIds.Contains(x.CategoryId), ct);

        if (total == 0)
        {
            return new AdminVotesPagedDto(0, [], skip, take, false);
        }

        // Sequential execution to avoid DbContext threading issues
        var votes = await _db.Votes
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.CategoryId))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var categories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        if (votes.Count == 0)
        {
            return new AdminVotesPagedDto(total, [], skip, take, false);
        }

        // Resolve user names in a single batch
        var userIds = votes.Select(x => x.UserId).Distinct().ToList();
        var usersById = await _db.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var enrichedVotes = votes.Select(vote =>
        {
            var categoryName = categories.TryGetValue(vote.CategoryId, out var cat)
                ? cat.Name
                : vote.CategoryId;

            var userName = usersById.TryGetValue(vote.UserId, out var user)
                ? GetUserName(user)
                : vote.UserId.ToString();

            return new AdminVoteAuditRowDto(
                vote.CategoryId,
                categoryName,
                vote.NomineeId,
                vote.UserId,
                userName,
                new DateTimeOffset(vote.UpdatedAtUtc, TimeSpan.Zero)
            );
        }).ToList();

        return new AdminVotesPagedDto(
            total,
            enrichedVotes,
            skip,
            take,
            skip + enrichedVotes.Count < total
        );
    }

    private async Task<AdminNomineesPagedDto> BuildAdminNominationsPagedDtoAsync(
        string eventId,
        string? normalizedStatus,
        int skip,
        int take,
        CancellationToken ct)
    {
        var query = _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        var total = await query.CountAsync(ct);

        var nominees = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        if (nominees.Count == 0)
        {
            return new AdminNomineesPagedDto(total, [], skip, take, false);
        }

        var submittedByIds = nominees
            .Select(x => x.SubmittedByUserId)
            .Distinct()
            .ToList();

        var usersById = await _db.Users
            .AsNoTracking()
            .Where(x => submittedByIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var dtos = nominees.Select(entity =>
        {
            var submittedByName = usersById.TryGetValue(entity.SubmittedByUserId, out var user)
                ? GetUserName(user)
                : entity.SubmittedByUserId.ToString();

            return ToAdminNomineeDto(entity, submittedByName);
        }).ToList();

        return new AdminNomineesPagedDto(
            total,
            dtos,
            skip,
            take,
            skip + dtos.Count < total
        );
    }

    private async Task<PagedResult<PublicUserDto>> LoadAdminMembersPagedAsync(
        string eventId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var directory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var members = directory.Members;
        var usersById = directory.UsersById;

        var allMembers = members
            .Where(member => usersById.ContainsKey(member.UserId))
            .OrderByDescending(member => member.Role == EventRoles.Admin)
            .ThenBy(member => GetUserName(usersById[member.UserId]))
            .Select(member => ToPublicUserDto(
                usersById[member.UserId],
                member.Role == EventRoles.Admin))
            .ToList();

        var total = allMembers.Count;
        var pagedMembers = allMembers.Skip(skip).Take(take).ToList();

        return new PagedResult<PublicUserDto>(
            pagedMembers,
            total,
            skip,
            take,
            skip + pagedMembers.Count < total
        );
    }

    private async Task<PagedResult<AdminCategoryResultDto>> BuildAdminOfficialResultsPagedDtoAsync(
        string eventId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var categories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Kind == AwardCategoryKind.UserVote)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        var directory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var usersById = directory.UsersById;
        var totalMembers = directory.Members
            .Select(member => member.UserId)
            .Distinct()
            .Count();

        var categoryIds = categories.Select(x => x.Id).ToList();
        var userVotes = categoryIds.Count == 0
            ? new List<UserVoteEntity>()
            : await _db.UserVotes
                .AsNoTracking()
                .Where(x => categoryIds.Contains(x.CategoryId))
                .ToListAsync(ct);

        var resultCategories = categories.Select(category =>
        {
            var votesForCategory = userVotes
                .Where(vote => vote.CategoryId == category.Id)
                .ToList();

            var nominees = votesForCategory
                .GroupBy(vote => vote.TargetUserId)
                .Select(group =>
                {
                    var nomineeTitle = usersById.TryGetValue(group.Key, out var nomineeUser)
                        ? GetUserName(nomineeUser)
                        : group.Key.ToString();

                    var voterUserIds = group
                        .Select(vote => usersById.TryGetValue(vote.VoterUserId, out var voterUser)
                            ? GetUserName(voterUser)
                            : vote.VoterUserId.ToString())
                        .Distinct()
                        .OrderBy(name => name)
                        .ToList();

                    return new AdminNomineeVoteTallyDto(
                        group.Key.ToString(),
                        nomineeTitle,
                        null,
                        group.Count(),
                        voterUserIds
                    );
                })
                .OrderByDescending(nominee => nominee.VoteCount)
                .ThenBy(nominee => nominee.NomineeTitle)
                .ToList();

            var totalVotes = votesForCategory.Count;
            var participationRate = totalMembers == 0 ? 0d : (double)totalVotes / totalMembers;

            return new AdminCategoryResultDto(
                category.Id,
                category.Name,
                totalVotes,
                nominees,
                participationRate
            );
        }).ToList();

        var total = resultCategories.Count;
        var pagedCategories = resultCategories.Skip(skip).Take(take).ToList();

        return new PagedResult<AdminCategoryResultDto>(
            pagedCategories,
            total,
            skip,
            take,
            skip + pagedCategories.Count < total
        );
    }

    // ---------- Summary methods (lightweight DTOs for list views) ----------

    private async Task<List<AwardCategorySummaryDto>> LoadAdminCategoriesSummaryAsync(
        string eventId,
        CancellationToken ct)
    {
        return await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(c => new AwardCategorySummaryDto(
                c.Id,
                c.Name,
                c.SortOrder,
                c.IsActive,
                c.Kind.ToString()
            ))
            .ToListAsync(ct);
    }

    private async Task<List<NomineeSummaryDto>> LoadAdminNomineesSummaryAsync(
        string eventId,
        string? normalizedStatus,
        CancellationToken ct)
    {
        var query = _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(n => new NomineeSummaryDto(
                n.Id,
                n.CategoryId,
                n.Title,
                n.Status
            ))
            .ToListAsync(ct);
    }

    private async Task<List<AdminNomineeSummaryDto>> LoadAdminNominationsSummaryAsync(
        string eventId,
        string? normalizedStatus,
        CancellationToken ct)
    {
        var query = _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            query = query.Where(x => x.Status == normalizedStatus);
        }

        var nominations = await (
            from nominee in query
            join user in _db.Users.AsNoTracking() on nominee.SubmittedByUserId equals user.Id into userGroup
            from user in userGroup.DefaultIfEmpty()
            orderby nominee.CreatedAtUtc descending
            select new
            {
                nominee.Id,
                nominee.CategoryId,
                nominee.Title,
                nominee.Status,
                nominee.SubmittedByUserId,
                DisplayName = user == null ? null : user.DisplayName,
                Email = user == null ? null : user.Email
            })
            .ToListAsync(ct);

        return nominations.Select(entry => new AdminNomineeSummaryDto(
                entry.Id,
                entry.CategoryId,
                entry.Title,
                entry.Status,
                entry.SubmittedByUserId,
                string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? entry.Email ?? entry.SubmittedByUserId.ToString()
                    : entry.DisplayName
            ))
            .ToList();
    }
}

internal static class EventsControllerMappers
{
    internal static EventSummaryDto ToEventSummaryDto(EventEntity entity) =>
        new(entity.Id, entity.Name, entity.IsActive);

    internal static EventPhaseDto ToEventPhaseDto(EventPhaseEntity entity) =>
        new(
            entity.Id,
            entity.Type,
            new DateTimeOffset(entity.StartDateUtc, TimeSpan.Zero),
            new DateTimeOffset(entity.EndDateUtc, TimeSpan.Zero),
            entity.IsActive
        );

    internal static EventCategoryDto ToEventCategoryDto(AwardCategoryEntity entity) =>
        new(entity.Id, entity.EventId, entity.Name, entity.Kind.ToString(), entity.IsActive, entity.Description);

    internal static AwardCategoryDto ToAwardCategoryDto(AwardCategoryEntity entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.SortOrder,
            entity.IsActive,
            entity.Kind.ToString(),
            entity.Description,
            entity.VoteQuestion,
            entity.VoteRules
        );

    internal static PublicUserDto ToPublicUserDto(UserEntity entity, bool isAdmin) =>
        new(entity.Id, entity.Email, entity.DisplayName, isAdmin);

    internal static NomineeDto ToNomineeDto(NomineeEntity entity) =>
        new(
            entity.Id,
            entity.CategoryId,
            entity.Title,
            entity.ImageUrl,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero)
        );

    internal static AdminNomineeDto ToAdminNomineeDto(
        NomineeEntity entity,
        string submittedByName) =>
        new(
            entity.Id,
            entity.CategoryId,
            entity.Title,
            entity.ImageUrl,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero),
            entity.SubmittedByUserId,
            submittedByName
        );

    internal static CategoryProposalDto ToCategoryProposalDto(CategoryProposalEntity entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero)
        );

    internal static MeasureProposalDto ToMeasureProposalDto(MeasureProposalEntity entity) =>
        new(
            entity.Id,
            entity.Text,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero)
        );

    internal static EventFeedPostDto ToEventFeedPostDto(
        HubPostEntity entity,
        string authorName,
        List<string> mediaUrls) =>
        new(
            entity.Id,
            entity.EventId,
            entity.AuthorUserId,
            authorName,
            entity.Text,
            mediaUrls.FirstOrDefault(),
            mediaUrls,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero)
        );

    internal static EventFeedPostFullDto ToEventFeedPostFullDto(
        HubPostEntity entity,
        string authorName,
        List<string> mediaUrls,
        int likeCount,
        int commentCount,
        int downvoteCount,
        Dictionary<string, int> reactionCounts,
        List<string> myReactions,
        bool likedByMe,
        bool downvotedByMe,
        EventFeedPollDto? poll) =>
        new(
            entity.Id,
            entity.EventId,
            entity.AuthorUserId.ToString(),
            authorName,
            entity.Text,
            mediaUrls.FirstOrDefault(),
            mediaUrls,
            entity.IsPinned,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero),
            likeCount,
            commentCount,
            downvoteCount,
            reactionCounts,
            myReactions,
            likedByMe,
            downvotedByMe,
            poll
        );

    internal static EventWishlistItemDto ToEventWishlistItemDto(WishlistItemEntity entity) =>
        new(
            entity.Id,
            entity.UserId,
            entity.EventId,
            entity.Title,
            entity.Url,
            entity.Notes,
            entity.ImageUrl,
            new DateTimeOffset(entity.UpdatedAtUtc, TimeSpan.Zero)
        );

    internal static EventProposalDto ToEventProposalDto(CategoryProposalEntity entity) =>
        new(
            entity.Id,
            entity.EventId,
            entity.ProposedByUserId,
            entity.Name,
            entity.Description,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero)
        );

    internal static string GetUserName(UserEntity user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName!;
}
