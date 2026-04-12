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
        CancellationToken ct)
    {
        var events = await LoadEventSummariesAsync(ct);
        var state = await BuildAdminStateDtoAsync(eventId, ct);
        var categories = await LoadAdminCategoriesAsync(eventId, ct);
        var nominees = await LoadAdminNomineeDtosAsync(eventId, null, ct);
        var adminNominees = await LoadAdminNominationDtosAsync(eventId, null, ct);
        var proposals = await BuildAdminProposalsHistoryDtoAsync(eventId, ct);
        var votes = await BuildAdminVotesDtoAsync(eventId, ct);
        var members = await LoadAdminMembersAsync(eventId, ct);
        var secretSanta = await BuildAdminSecretSantaStateDtoAsync(eventId, null, ct);
        var officialResults = await BuildAdminOfficialResultsDtoAsync(eventId, ct);

        return new EventAdminBootstrapDto(
            events,
            state,
            categories,
            nominees,
            adminNominees,
            proposals,
            votes,
            members,
            secretSanta,
            officialResults
        );
    }

    private async Task<EventCountsDto> BuildAdminCountsDtoAsync(
        string eventId,
        CancellationToken ct)
    {
        return new EventCountsDto(
            await _db.EventMembers.AsNoTracking().CountAsync(x => x.EventId == eventId, ct),
            await _db.HubPosts.AsNoTracking().CountAsync(x => x.EventId == eventId, ct),
            await _db.AwardCategories.AsNoTracking().CountAsync(x => x.EventId == eventId, ct),
            await _db.CategoryProposals.AsNoTracking().CountAsync(
                x => x.EventId == eventId && x.Status == "pending",
                ct),
            await _db.WishlistItems.AsNoTracking().CountAsync(x => x.EventId == eventId, ct)
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
        var categoryIds = await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var votes = await _db.Votes
            .AsNoTracking()
            .Where(x => categoryIds.Contains(x.CategoryId))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(ct);

        return new AdminVotesDto(votes.Count, votes.Select(ToAdminVoteAuditRowDto).ToList());
    }

    private async Task<AdminOfficialResultsDto> BuildAdminOfficialResultsDtoAsync(
        string eventId,
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

    private static AdminVoteAuditRowDto ToAdminVoteAuditRowDto(VoteEntity entity) =>
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
                "pending",
                StringComparison.OrdinalIgnoreCase)).ToList(),
            items.Where(item => string.Equals(
                statusSelector(item),
                "approved",
                StringComparison.OrdinalIgnoreCase)).ToList(),
            items.Where(item => string.Equals(
                statusSelector(item),
                "rejected",
                StringComparison.OrdinalIgnoreCase)).ToList()
        );

    private static string GetUserName(UserEntity user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName!;
}
