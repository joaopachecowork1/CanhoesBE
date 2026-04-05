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
        var moduleAccess = await EventModuleAccessEvaluator.EvaluateAsync(
            _db,
            eventId,
            Guid.Empty,
            isAdmin: true,
            ct);

        var activePhaseEntity = moduleAccess.ActivePhase ?? phases.FirstOrDefault(x => x.IsActive);
        var activePhase = activePhaseEntity is null ? null : ToEventPhaseDto(activePhaseEntity);

        return new EventAdminStateDto(
            eventId,
            activePhase,
            phases.Select(ToEventPhaseDto).ToList(),
            legacyState.NominationsVisible,
            legacyState.ResultsVisible,
            moduleAccess.ModuleVisibility,
            moduleAccess.EffectiveModules,
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
        var proposals = await BuildAdminProposalsHistoryDtoAsync(eventId, ct);
        var votes = await BuildAdminVotesDtoAsync(eventId, ct);
        var members = await LoadAdminMembersAsync(eventId, ct);
        var secretSanta = await BuildAdminSecretSantaStateDtoAsync(eventId, null, ct);

        return new EventAdminBootstrapDto(
            events,
            state,
            categories,
            nominees,
            proposals,
            votes,
            members,
            secretSanta
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
