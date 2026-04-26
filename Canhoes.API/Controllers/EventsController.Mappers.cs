using Canhoes.Api.Access;
using Canhoes.Api.Models;
using Canhoes.Api.DTOs;
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
        var eventPhases = await LoadEventPhasesAsync(eventId, ct);
        var memberModuleAccess = await EventModuleAccessEvaluator.EvaluateAsync(
            _db,
            eventId,
            Guid.Empty,
            isAdmin: false,
            ct);

        var activePhaseEntity = memberModuleAccess.ActivePhase ?? eventPhases.FirstOrDefault(x => x.IsActive);
        var activeEventPhaseDto = activePhaseEntity is null ? null : ToEventPhaseDto(activePhaseEntity);

        return new EventAdminStateDto(
            eventId,
            activeEventPhaseDto,
            eventPhases.Select(ToEventPhaseDto).ToList(),
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
        var latestSecretSantaDraw = string.IsNullOrWhiteSpace(requestedEventCode)
            ? await GetLatestSecretSantaDrawAsync(eventId, ct)
            : await _db.SecretSantaDraws
                .AsNoTracking()
                .Where(x => x.EventCode == requestedEventCode)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

        var secretSantaEventCode = latestSecretSantaDraw?.EventCode ?? NormalizeSecretSantaEventCode(eventId, requestedEventCode);
        var totalMemberCount = await _db.EventMembers.AsNoTracking().CountAsync(x => x.EventId == eventId, ct);
        var totalAssignmentCount = latestSecretSantaDraw is null
            ? 0
            : await _db.SecretSantaAssignments.AsNoTracking().CountAsync(x => x.DrawId == latestSecretSantaDraw.Id, ct);

        return new EventAdminSecretSantaStateDto(
            eventId,
            secretSantaEventCode,
            latestSecretSantaDraw is not null,
            latestSecretSantaDraw?.Id,
            latestSecretSantaDraw is null ? null : new DateTimeOffset(latestSecretSantaDraw.CreatedAtUtc, TimeSpan.Zero),
            latestSecretSantaDraw?.IsLocked ?? false,
            totalMemberCount,
            totalAssignmentCount
        );
    }

    private async Task<EventAdminBootstrapDto> BuildAdminBootstrapDtoAsync(
        string eventId,
        bool includeLists,
        CancellationToken ct)
    {
        var allEventSummaries = await LoadEventSummariesAsync(ct);
        var adminState = await BuildAdminStateDtoAsync(eventId, ct);
        var listCounts = await BuildAdminListCountsDtoAsync(eventId, ct);

        return new EventAdminBootstrapDto(
            allEventSummaries,
            adminState,
            listCounts
        );
    }

    private async Task<AdminListCountsDto> BuildAdminListCountsDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        // Sequential execution to avoid DbContext threading issues
        var awardCategoryIds = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var proposalsSummary = await _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Pending = g.Count(x => x.Status == ProposalStatus.Pending)
            })
            .FirstOrDefaultAsync(ct);

        var measureProposalsSummary = await _db.MeasureProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Pending = g.Count(x => x.Status == ProposalStatus.Pending)
            })
            .FirstOrDefaultAsync(ct);

        var totalNomineesCount = await _db.Nominees
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var totalMembersCount = await _db.EventMembers
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var voteCategoriesCount = await _db.AwardCategories
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId && x.Kind == AwardCategoryKind.UserVote, ct);

        var totalVotesCount = awardCategoryIds.Count == 0
            ? 0
            : await _db.Votes
                .AsNoTracking()
                .CountAsync(x => awardCategoryIds.Contains(x.CategoryId), ct);

        return new AdminListCountsDto(
            totalNomineesCount,
            totalNomineesCount,
            totalVotesCount,
            proposalsSummary?.Total ?? 0,
            proposalsSummary?.Pending ?? 0,
            measureProposalsSummary?.Total ?? 0,
            measureProposalsSummary?.Pending ?? 0,
            totalMembersCount,
            voteCategoriesCount
        );
    }

    private async Task<EventCountsDto> BuildAdminCountsDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        // Reuse the consolidated single-query counts from LoadEventCountsAsync,
        // then fetch the category count separately (different filter).
        var aggregateCounts = await LoadEventCountsAsync(eventId, ct);
        var totalCategoryCount = await _db.AwardCategories
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        return new EventCountsDto(
            aggregateCounts.MemberCount,
            aggregateCounts.HubPostCount,
            totalCategoryCount,
            aggregateCounts.PendingCategoryProposalsCount,
            aggregateCounts.WishlistCount
        );
    }

    private async Task<List<AwardCategoryDto>> LoadAdminCategoriesAsync(
        string eventId,
        CancellationToken ct)
    {
        var awardCategories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        return awardCategories.Select(ToAwardCategoryDto).ToList();
    }

    private async Task<AdminNomineeDto> BuildAdminNominationDtoAsync(
        NomineeEntity entity,
        CancellationToken ct)
    {
        var submittedByUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == entity.SubmittedByUserId, ct);

        return ToAdminNomineeDto(
            entity,
            submittedByUser is null ? entity.SubmittedByUserId.ToString() : GetUserName(submittedByUser)
        );
    }

    // ---------- Paginated methods ----------

    private async Task<AdminVotesPagedDto> BuildAdminVotesPagedDtoAsync(
        string eventId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var awardCategoryIds = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var totalVotesCount = awardCategoryIds.Count == 0
            ? 0
            : await _db.Votes
                .AsNoTracking()
                .CountAsync(x => awardCategoryIds.Contains(x.CategoryId), ct);

        if (totalVotesCount == 0)
        {
            return new AdminVotesPagedDto(0, [], skip, take, false);
        }

        var pagedVotes = await _db.Votes
            .AsNoTracking()
            .Where(x => awardCategoryIds.Contains(x.CategoryId))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var awardCategoriesLookup = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => awardCategoryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        if (pagedVotes.Count == 0)
        {
            return new AdminVotesPagedDto(totalVotesCount, [], skip, take, false);
        }

        // Resolve user names in a single batch
        var voterUserIds = pagedVotes.Select(x => x.UserId).Distinct().ToList();
        var votersLookup = await _db.Users
            .AsNoTracking()
            .Where(x => voterUserIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var enrichedVotes = pagedVotes.Select(vote =>
        {
            var categoryName = awardCategoriesLookup.TryGetValue(vote.CategoryId, out var cat)
                ? cat.Name
                : vote.CategoryId;

            var userName = votersLookup.TryGetValue(vote.UserId, out var user)
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
            totalVotesCount,
            enrichedVotes,
            skip,
            take,
            skip + enrichedVotes.Count < totalVotesCount
        );
    }

    private async Task<AdminNomineesPagedDto> BuildAdminNominationsPagedDtoAsync(
        string eventId,
        string? normalizedStatus,
        int skip,
        int take,
        CancellationToken ct)
    {
        var nominationsQuery = _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            nominationsQuery = nominationsQuery.Where(x => x.Status == normalizedStatus);
        }

        var totalNominationsCount = await nominationsQuery.CountAsync(ct);

        var pagedNominees = await nominationsQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        if (pagedNominees.Count == 0)
        {
            return new AdminNomineesPagedDto(totalNominationsCount, [], skip, take, false);
        }

        var submittedByUserIds = pagedNominees
            .Select(x => x.SubmittedByUserId)
            .Distinct()
            .ToList();

        var usersLookup = await _db.Users
            .AsNoTracking()
            .Where(x => submittedByUserIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var nominationDtos = pagedNominees.Select(entity =>
        {
            var submittedByName = usersLookup.TryGetValue(entity.SubmittedByUserId, out var user)
                ? GetUserName(user)
                : entity.SubmittedByUserId.ToString();

            return ToAdminNomineeDto(entity, submittedByName);
        }).ToList();

        return new AdminNomineesPagedDto(
            totalNominationsCount,
            nominationDtos,
            skip,
            take,
            skip + nominationDtos.Count < totalNominationsCount
        );
    }

    private async Task<PagedResult<PublicUserDto>> LoadAdminMembersPagedAsync(
        string eventId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var memberDirectory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var members = memberDirectory.Members;
        var usersLookup = memberDirectory.UsersById;

        var allMembers = members
            .Where(member => usersLookup.ContainsKey(member.UserId))
            .OrderByDescending(member => member.Role == EventRoles.Admin)
            .ThenBy(member => GetUserName(usersLookup[member.UserId]))
            .Select(member => ToPublicUserDto(
                usersLookup[member.UserId],
                member.Role == EventRoles.Admin))
            .ToList();

        var totalCount = allMembers.Count;
        var pagedMembers = allMembers.Skip(skip).Take(take).ToList();

        return new PagedResult<PublicUserDto>(
            pagedMembers,
            totalCount,
            skip,
            take,
            skip + pagedMembers.Count < totalCount
        );
    }

    private async Task<PagedResult<AdminCategoryResultDto>> BuildAdminOfficialResultsPagedDtoAsync(
        string eventId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var memberDirectory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var totalMembersInEvent = memberDirectory.Members.Count;

        var resultsQuery = _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Kind == AwardCategoryKind.UserVote)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name);

        var totalCategoriesCount = await resultsQuery.CountAsync(ct);

        var pagedResultsData = await resultsQuery
            .Skip(skip)
            .Take(take)
            .Select(c => new
            {
                c.Id,
                c.Name,
                Votes = _db.UserVotes
                    .Where(v => v.CategoryId == c.Id)
                    .GroupBy(v => v.TargetUserId)
                    .Select(g => new
                    {
                        TargetUserId = g.Key,
                        VoteCount = g.Count(),
                        Voters = g.Select(v => _db.Users
                            .Where(u => u.Id == v.VoterUserId)
                            .Select(u => u.DisplayName ?? u.Email)
                            .FirstOrDefault())
                            .ToList()
                    })
                    .ToList()
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var categoryResultDtos = pagedResultsData.Select(c =>
        {
            var voteTallies = c.Votes.Select(v => new AdminNomineeVoteTallyDto(
                v.TargetUserId.ToString(),
                memberDirectory.UsersById.TryGetValue(v.TargetUserId, out var user) ? GetUserName(user) : v.TargetUserId.ToString(),
                null,
                v.VoteCount,
                v.Voters.Where(name => name != null).Cast<string>().ToList()
            ))
            .OrderByDescending(n => n.VoteCount)
            .ToList();

            var totalVotesInCategory = voteTallies.Sum(n => n.VoteCount);
            return new AdminCategoryResultDto(
                c.Id,
                c.Name,
                totalVotesInCategory,
                voteTallies,
                totalMembersInEvent == 0 ? 0d : (double)totalVotesInCategory / totalMembersInEvent
            );
        }).ToList();

        return new PagedResult<AdminCategoryResultDto>(
            categoryResultDtos,
            totalCategoriesCount,
            skip,
            take,
            skip + categoryResultDtos.Count < totalCategoriesCount
        );
    }

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
        var summariesQuery = _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            summariesQuery = summariesQuery.Where(x => x.Status == normalizedStatus);
        }

        return await summariesQuery
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
        var nominationsSummaryQuery = _db.Nominees
            .AsNoTracking()
            .Where(x => x.EventId == eventId);

        if (normalizedStatus is not null)
        {
            nominationsSummaryQuery = nominationsSummaryQuery.Where(x => x.Status == normalizedStatus);
        }

        var pagedNominationsSummary = await (
            from nominee in nominationsSummaryQuery
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

        return pagedNominationsSummary.Select(entry => new AdminNomineeSummaryDto(
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
