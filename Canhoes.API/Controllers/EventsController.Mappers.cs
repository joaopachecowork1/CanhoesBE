using Canhoes.Api.Access;
using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    private async Task<List<EventSummaryDto>> LoadEventSummariesAsync(CancellationToken ct)
    {
        var events = await _db.Events
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        return events.Select(ToEventSummaryDto).ToList();
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
        var events = await LoadEventSummariesAsync(ct);
        var state = await BuildAdminStateDtoAsync(eventId, ct);
        var counts = await BuildAdminListCountsDtoAsync(eventId, ct);

        if (!includeLists)
        {
            return new EventAdminBootstrapDto(
                events,
                state,
                counts
            );
        }

        // OPTIMIZATION: Parallelize independent data loads
        // Group 1: Categories + Members + SecretSanta (independent reads)
        var categoriesTask = LoadAdminCategoriesAsync(eventId, ct);
        var membersTask = LoadAdminMembersAsync(eventId, ct);
        var secretSantaTask = BuildAdminSecretSantaStateDtoAsync(eventId, null, ct);

        // Group 2: Nominees + Proposals (independent reads)
        var nomineesTask = LoadAdminNomineeDtosAsync(eventId, null, ct);
        var adminNomineesTask = LoadAdminNominationDtosAsync(eventId, null, ct);
        var proposalsTask = BuildAdminProposalsHistoryDtoAsync(eventId, ct);

        // Group 3: Votes + OfficialResults (reads UserVotes independently)
        var votesTask = BuildAdminVotesDtoAsync(eventId, ct);
        var officialResultsTask = BuildAdminOfficialResultsDtoAsync(eventId, ct);

        // Execute all groups in parallel
        await Task.WhenAll(
            categoriesTask, membersTask, secretSantaTask,
            nomineesTask, adminNomineesTask, proposalsTask,
            votesTask, officialResultsTask);

        return new EventAdminBootstrapDto(
            events,
            state,
            counts,
            await categoriesTask,
            await nomineesTask,
            await adminNomineesTask,
            await proposalsTask,
            await votesTask,
            await membersTask,
            await secretSantaTask,
            await officialResultsTask
        );
    }

    private async Task<AdminListCountsDto> BuildAdminListCountsDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        // Parallelize independent count queries
        var categoryIdsTask = _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var categoryProposalsTask = _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Pending = g.Count(x => x.Status == ProposalStatus.Pending)
            })
            .FirstOrDefaultAsync(ct);

        var measureProposalsTask = _db.MeasureProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Pending = g.Count(x => x.Status == ProposalStatus.Pending)
            })
            .FirstOrDefaultAsync(ct);

        var nomineesTask = _db.Nominees
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var membersTask = _db.EventMembers
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var voteCategoriesTask = _db.AwardCategories
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId && x.Kind == AwardCategoryKind.UserVote, ct);

        await Task.WhenAll(
            categoryIdsTask,
            categoryProposalsTask,
            measureProposalsTask,
            nomineesTask,
            membersTask,
            voteCategoriesTask);

        var categoryIds = categoryIdsTask.Result;
        var categoryProposals = categoryProposalsTask.Result;
        var measureProposals = measureProposalsTask.Result;

        var votesTotal = categoryIds.Count == 0
            ? 0
            : await _db.Votes
                .AsNoTracking()
                .CountAsync(x => categoryIds.Contains(x.CategoryId), ct);

        return new AdminListCountsDto(
            nomineesTask.Result,
            nomineesTask.Result, // adminNomineesTotal = same source
            votesTotal,
            categoryProposals?.Total ?? 0,
            categoryProposals?.Pending ?? 0,
            measureProposals?.Total ?? 0,
            measureProposals?.Pending ?? 0,
            membersTask.Result,
            voteCategoriesTask.Result
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

    private async Task<List<NomineeDto>> LoadAdminNomineeDtosAsync(
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

        var nominees = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return nominees.Select(ToNomineeDto).ToList();
    }

    private async Task<List<AdminNomineeDto>> LoadAdminNominationDtosAsync(
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

        var nominees = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var submittedByIds = nominees
            .Select(x => x.SubmittedByUserId)
            .Distinct()
            .ToList();

        var usersById = submittedByIds.Count == 0
            ? new Dictionary<Guid, UserEntity>()
            : await _db.Users
                .AsNoTracking()
                .Where(x => submittedByIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        return nominees.Select(entity =>
        {
            var submittedByName = usersById.TryGetValue(entity.SubmittedByUserId, out var user)
                ? GetUserName(user)
                : entity.SubmittedByUserId.ToString();

            return ToAdminNomineeDto(entity, submittedByName);
        }).ToList();
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

    private async Task<AdminProposalsHistoryDto> BuildAdminProposalsHistoryDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        var categoryProposals = await _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var measureProposals = await _db.MeasureProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return new AdminProposalsHistoryDto(
            BuildProposalHistory(
                categoryProposals.Select(ToCategoryProposalDto).ToList(),
                proposal => proposal.Status),
            BuildProposalHistory(
                measureProposals.Select(ToMeasureProposalDto).ToList(),
                proposal => proposal.Status)
        );
    }

    private async Task<AdminVotesDto> BuildAdminVotesDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        var categoryIdsTask = _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        await categoryIdsTask;
        var categoryIds = categoryIdsTask.Result;

        if (categoryIds.Count == 0)
        {
            return new AdminVotesDto(0, []);
        }

        // OPTIMIZATION: Limit to most recent 500 votes to prevent memory overload
        // Use the paginated endpoint ({eventId}/admin/votes/paged) for full access
        const int maxVotes = 500;
        var votesTask = _db.Votes
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.CategoryId))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(maxVotes)
            .ToListAsync(ct);

        var categoriesTask = _db.AwardCategories
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        await Task.WhenAll(votesTask, categoriesTask);

        var votes = votesTask.Result;
        if (votes.Count == 0)
        {
            return new AdminVotesDto(0, []);
        }

        // Resolve user names in a single batch
        var userIds = votes.Select(x => x.UserId).Distinct().ToList();
        var usersById = await _db.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var enrichedVotes = votes.Select(vote =>
        {
            var categoryName = categoriesTask.Result.TryGetValue(vote.CategoryId, out var cat)
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

        return new AdminVotesDto(votes.Count, enrichedVotes);
    }

    private async Task<AdminOfficialResultsDto> BuildAdminOfficialResultsDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        // Load categories and member directory in parallel
        var categoriesTask = _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Kind == AwardCategoryKind.UserVote)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        var directoryTask = LoadEventMemberDirectoryAsync(eventId, ct);

        await Task.WhenAll(categoriesTask, directoryTask);

        var categories = categoriesTask.Result;
        var directory = directoryTask.Result;
        var usersById = directory.UsersById;
        var totalMembers = directory.Members
            .Select(member => member.UserId)
            .Distinct()
            .Count();

        var categoryIds = categories.Select(x => x.Id).ToList();
        if (categoryIds.Count == 0)
        {
            return new AdminOfficialResultsDto(
                eventId,
                DateTimeOffset.UtcNow,
                totalMembers,
                new List<AdminCategoryResultDto>());
        }

        // OPTIMIZATION: Project only needed columns instead of full entities
        var userVotes = await _db.UserVotes
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.CategoryId))
            .Select(x => new { x.CategoryId, x.VoterUserId, x.TargetUserId })
            .ToListAsync(ct);

        // Pre-build user name lookup for all user IDs that appear in votes
        var allUserIds = userVotes
            .Select(v => v.VoterUserId)
            .Concat(userVotes.Select(v => v.TargetUserId))
            .Distinct()
            .ToList();

        var userNameLookup = new Dictionary<Guid, string>();
        foreach (var userId in allUserIds)
        {
            if (usersById.TryGetValue(userId, out var user))
            {
                userNameLookup[userId] = GetUserName(user);
            }
            else
            {
                userNameLookup[userId] = userId.ToString();
            }
        }

        // OPTIMIZATION: Pre-group votes by category for O(1) lookup
        var votesByCategory = userVotes
            .GroupBy(v => v.CategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var resultCategories = categories.Select(category =>
        {
            if (!votesByCategory.TryGetValue(category.Id, out var votesForCategory))
            {
                var emptyTotalVotes = 0;
                var emptyParticipationRate = totalMembers == 0 ? 0d : (double)emptyTotalVotes / totalMembers;
                return new AdminCategoryResultDto(
                    category.Id,
                    category.Name,
                    emptyTotalVotes,
                    new List<AdminNomineeVoteTallyDto>(),
                    emptyParticipationRate);
            }

            // Group votes by target user (nominee)
            var votesByNominee = votesForCategory
                .GroupBy(v => v.TargetUserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var nominees = votesByNominee
                .Select(kvp =>
                {
                    var nomineeId = kvp.Key;
                    var votesForNominee = kvp.Value;
                    var voteCount = votesForNominee.Count;

                    var nomineeTitle = userNameLookup.TryGetValue(nomineeId, out var name)
                        ? name
                        : nomineeId.ToString();

                    var voterUserIds = votesForNominee
                        .Select(v => v.VoterUserId)
                        .Select(voterId => userNameLookup.TryGetValue(voterId, out var voterName)
                            ? voterName
                            : voterId.ToString())
                        .Distinct()
                        .OrderBy(n => n)
                        .ToList();

                    return new AdminNomineeVoteTallyDto(
                        nomineeId.ToString(),
                        nomineeTitle,
                        null,
                        voteCount,
                        voterUserIds
                    );
                })
                .OrderByDescending(n => n.VoteCount)
                .ThenBy(n => n.NomineeTitle)
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

        return new AdminOfficialResultsDto(
            eventId,
            DateTimeOffset.UtcNow,
            totalMembers,
            resultCategories
        );
    }

    private async Task<List<PublicUserDto>> LoadAdminMembersAsync(
        string eventId,
        CancellationToken ct)
    {
        var directory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var members = directory.Members;
        var usersById = directory.UsersById;

        return members
            .Where(member => usersById.ContainsKey(member.UserId))
            .OrderByDescending(member => member.Role == EventRoles.Admin)
            .ThenBy(member => GetUserName(usersById[member.UserId]))
            .Select(member => ToPublicUserDto(
                usersById[member.UserId],
                member.Role == EventRoles.Admin))
            .ToList();
    }

    private static EventSummaryDto ToEventSummaryDto(EventEntity entity) =>
        new(entity.Id, entity.Name, entity.IsActive);

    private static EventPhaseDto ToEventPhaseDto(EventPhaseEntity entity) =>
        new(
            entity.Id,
            entity.Type,
            new DateTimeOffset(entity.StartDateUtc, TimeSpan.Zero),
            new DateTimeOffset(entity.EndDateUtc, TimeSpan.Zero),
            entity.IsActive
        );

    private static EventCategoryDto ToEventCategoryDto(AwardCategoryEntity entity) =>
        new(entity.Id, entity.EventId, entity.Name, entity.Kind.ToString(), entity.IsActive, entity.Description);

    private static AwardCategoryDto ToAwardCategoryDto(AwardCategoryEntity entity) =>
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

    private static PublicUserDto ToPublicUserDto(UserEntity entity, bool isAdmin) =>
        new(entity.Id, entity.Email, entity.DisplayName, isAdmin);

    private static NomineeDto ToNomineeDto(NomineeEntity entity) =>
        new(
            entity.Id,
            entity.CategoryId,
            entity.Title,
            entity.ImageUrl,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero)
        );

    private static AdminNomineeDto ToAdminNomineeDto(
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

    private static CategoryProposalDto ToCategoryProposalDto(CategoryProposalEntity entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero)
        );

    private static MeasureProposalDto ToMeasureProposalDto(MeasureProposalEntity entity) =>
        new(
            entity.Id,
            entity.Text,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero)
        );

    private static EventFeedPostDto ToEventFeedPostDto(
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

    private static EventFeedPostFullDto ToEventFeedPostFullDto(
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

    /// <summary>
    /// @deprecated Use enriched BuildAdminVotesDtoAsync instead, which resolves
    /// category and user names. Kept for legacy compatibility only.
    /// </summary>
    private static AdminVoteAuditRowLegacyDto ToAdminVoteAuditRowDto(VoteEntity entity) =>
        new(
            entity.CategoryId,
            entity.NomineeId,
            entity.UserId,
            new DateTimeOffset(entity.UpdatedAtUtc, TimeSpan.Zero)
        );

    private static EventWishlistItemDto ToEventWishlistItemDto(WishlistItemEntity entity) =>
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

    private static EventProposalDto ToEventProposalDto(CategoryProposalEntity entity) =>
        new(
            entity.Id,
            entity.EventId,
            entity.ProposedByUserId,
            entity.Name,
            entity.Description,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero)
        );

    private static ProposalsByStatusDto<T> BuildProposalHistory<T>(
        List<T> items,
        Func<T, string> statusSelector) =>
        new(
            items.Where(item => string.Equals(
                statusSelector(item),
                ProposalStatus.Pending,
                StringComparison.OrdinalIgnoreCase)).ToList(),
            items.Where(item => string.Equals(
                statusSelector(item),
                ProposalStatus.Approved,
                StringComparison.OrdinalIgnoreCase)).ToList(),
            items.Where(item => string.Equals(
                statusSelector(item),
                ProposalStatus.Rejected,
                StringComparison.OrdinalIgnoreCase)).ToList()
        );

    private static string GetUserName(UserEntity user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName!;

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

        // Load paged votes and categories in parallel
        var votesTask = _db.Votes
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.CategoryId))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var categoriesTask = _db.AwardCategories
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        await Task.WhenAll(votesTask, categoriesTask);

        var votes = votesTask.Result;
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
            var categoryName = categoriesTask.Result.TryGetValue(vote.CategoryId, out var cat)
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
            skip + take < total
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
            skip + take < total
        );
    }

    private async Task<AdminProposalsPagedDto> BuildAdminProposalsPagedDtoAsync(
        string eventId,
        int skip,
        int take,
        CancellationToken ct)
    {
        var categoryProposalsTotal = await _db.CategoryProposals
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var measureProposalsTotal = await _db.MeasureProposals
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var categoryProposals = await _db.CategoryProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var measureProposals = await _db.MeasureProposals
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return new AdminProposalsPagedDto(
            categoryProposalsTotal,
            BuildProposalHistory(
                categoryProposals.Select(ToCategoryProposalDto).ToList(),
                p => p.Status),
            measureProposalsTotal,
            BuildProposalHistory(
                measureProposals.Select(ToMeasureProposalDto).ToList(),
                p => p.Status)
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
            skip + take < total
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
            skip + take < total
        );
    }

    // ---------- Summary methods (lightweight DTOs for list views) ----------

    private async Task<List<AwardCategorySummaryDto>> LoadAdminCategoriesSummaryAsync(
        string eventId,
        CancellationToken ct)
    {
        var categories = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        return categories.Select(c => new AwardCategorySummaryDto(
            c.Id,
            c.Name,
            c.SortOrder,
            c.IsActive,
            c.Kind.ToString()
        )).ToList();
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

        var nominees = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return nominees.Select(n => new NomineeSummaryDto(
            n.Id,
            n.CategoryId,
            n.Title,
            n.Status
        )).ToList();
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

        var nominees = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        if (nominees.Count == 0) return [];

        var submittedByIds = nominees
            .Select(x => x.SubmittedByUserId)
            .Distinct()
            .ToList();

        var usersById = await _db.Users
            .AsNoTracking()
            .Where(x => submittedByIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        return nominees.Select(entity =>
        {
            var submittedByName = usersById.TryGetValue(entity.SubmittedByUserId, out var user)
                ? GetUserName(user)
                : entity.SubmittedByUserId.ToString();

            return new AdminNomineeSummaryDto(
                entity.Id,
                entity.CategoryId,
                entity.Title,
                entity.Status,
                entity.SubmittedByUserId,
                submittedByName
            );
        }).ToList();
    }
}
