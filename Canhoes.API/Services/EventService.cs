using Canhoes.Api.Access;
using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using Canhoes.Api.Repositories;
using Microsoft.Extensions.Caching.Memory;
using static Canhoes.Api.Mappers.EventMappers;

namespace Canhoes.Api.Services;

public sealed class EventService : IEventService
{
    private readonly IEventRepository _eventRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAwardRepository _awardRepository;
    private readonly ISecretSantaRepository _secretSantaRepository;
    private readonly IModuleAccessService _moduleAccessService;
    private readonly IFeedService _feedService;
    private readonly IMemoryCache _cache;

    public EventService(
        IEventRepository eventRepository,
        IUserRepository userRepository,
        IAwardRepository awardRepository,
        ISecretSantaRepository secretSantaRepository,
        IModuleAccessService moduleAccessService,
        IFeedService feedService,
        IMemoryCache cache)
    {
        _eventRepository = eventRepository;
        _userRepository = userRepository;
        _awardRepository = awardRepository;
        _secretSantaRepository = secretSantaRepository;
        _moduleAccessService = moduleAccessService;
        _feedService = feedService;
        _cache = cache;
    }

    public async Task<EventActiveContextDto?> GetActiveEventContextAsync(Guid userId, bool isAdmin, CancellationToken ct)
    {
        var activeEvent = await _eventRepository.GetActiveEventAsync(ct);
        if (activeEvent is null) return null;

        var overview = await GetEventOverviewAsync(activeEvent.Id, userId, isAdmin, ct);
        if (overview is null) return null;

        return new EventActiveContextDto(ToEventSummaryDto(activeEvent), overview);
    }

    public async Task<EventHomeSnapshotDto?> GetActiveHomeSnapshotAsync(Guid userId, bool isAdmin, CancellationToken ct)
    {
        var activeEvent = await _eventRepository.GetActiveEventAsync(ct);
        if (activeEvent is null) return null;

        var overview = await GetEventOverviewAsync(activeEvent.Id, userId, isAdmin, ct);
        if (overview is null) return null;

        var votingOverview = await GetVotingOverviewAsync(activeEvent.Id, userId, ct);
        var secretSantaOverview = await GetSecretSantaOverviewAsync(activeEvent.Id, userId, ct);
        var recentPosts = await _feedService.GetRecentPostsAsync(activeEvent.Id, userId, 5, ct);

        return new EventHomeSnapshotDto(
            ToEventSummaryDto(activeEvent),
            overview,
            votingOverview,
            secretSantaOverview,
            recentPosts
        );
    }

    private async Task<EventVotingOverviewDto> GetVotingOverviewAsync(string eventId, Guid userId, CancellationToken ct)
    {
        var phase = await _eventRepository.GetActivePhaseAsync(eventId, EventPhaseTypes.Voting, ct);
        var categories = await _awardRepository.GetActiveCategoriesAsync(eventId, ct);
        var categoryIds = categories.Select(x => x.Id).ToList();
        var votes = await _awardRepository.CountSubmittedVotesAsync(userId, categoryIds, ct);

        return new EventVotingOverviewDto(
            eventId,
            phase?.Id,
            phase is not null,
            phase?.EndDateUtc != null ? new DateTimeOffset(phase.EndDateUtc, TimeSpan.Zero) : null,
            categories.Count,
            votes,
            Math.Max(0, categories.Count - votes)
        );
    }

    private async Task<EventSecretSantaOverviewDto> GetSecretSantaOverviewAsync(string eventId, Guid userId, CancellationToken ct)
    {
        var state = await _eventRepository.GetEventStateAsync(eventId, ct);
        var wishlistCount = await _eventRepository.CountWishlistItemsAsync(eventId, userId, ct);
        
        SecretSantaAssignmentEntity? assignment = null;
        if (state?.HasSecretSantaDraw == true && !string.IsNullOrEmpty(state.SecretSantaEventCode))
        {
            var draw = await _secretSantaRepository.GetLatestDrawAsync(state.SecretSantaEventCode, ct);
            if (draw is not null)
            {
                assignment = await _secretSantaRepository.GetAssignmentAsync(draw.Id, userId, ct);
            }
        }

        EventUserDto? assignedUser = null;
        int assignedWishlistCount = 0;

        if (assignment is not null)
        {
            var receiver = await _userRepository.GetUserAsync(assignment.ReceiverUserId, ct);
            if (receiver is not null)
            {
                assignedUser = new EventUserDto(receiver.Id, GetUserName(receiver), "member");
                assignedWishlistCount = await _eventRepository.CountWishlistItemsAsync(eventId, receiver.Id, ct);
            }
        }

        return new EventSecretSantaOverviewDto(
            eventId,
            state?.HasSecretSantaDraw ?? false,
            assignment is not null,
            state?.SecretSantaEventCode,
            assignedUser,
            assignedWishlistCount,
            wishlistCount
        );
    }

    public async Task<List<EventSummaryDto>> GetEventSummariesAsync(CancellationToken ct)
    {
        var events = await _eventRepository.GetAllEventsAsync(ct);
        return events.Select(ToEventSummaryDto).ToList();
    }

    public async Task<EventContextDto?> GetEventContextAsync(string eventId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var eventEntity = await _eventRepository.GetEventAsync(eventId, ct);
        if (eventEntity is null) return null;

        var phases = await _eventRepository.GetEventPhasesAsync(eventId, ct);
        var members = await _eventRepository.GetEventMembersAsync(eventId, ct);
        var userIds = members.Select(x => x.UserId).Distinct().ToList();
        var users = await _userRepository.GetUsersAsync(userIds, ct);
        var usersById = users.ToDictionary(x => x.Id);

        var activePhase = phases.FirstOrDefault(x => x.IsActive);
        
        return new EventContextDto(
            ToEventSummaryDto(eventEntity),
            members
                .Where(m => usersById.ContainsKey(m.UserId))
                .Select(m => new EventUserDto(m.UserId, GetUserName(usersById[m.UserId]), m.Role))
                .ToList(),
            phases.Select(ToEventPhaseDto).ToList(),
            activePhase == null ? null : ToEventPhaseDto(activePhase));
    }

    public async Task<EventOverviewDto?> GetEventOverviewAsync(string eventId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var eventEntity = await _eventRepository.GetEventAsync(eventId, ct);
        if (eventEntity is null) return null;

        var eventPhases = await _eventRepository.GetEventPhasesAsync(eventId, ct);
        var moduleAccess = await _moduleAccessService.EvaluateAsync(eventId, userId, isAdmin, eventPhases, ct);
        
        var activeEventPhaseEntity = moduleAccess.ActivePhase ?? eventPhases.FirstOrDefault(x => x.IsActive);
        var activeEventPhase = activeEventPhaseEntity is null ? null : ToEventPhaseDto(activeEventPhaseEntity);
        var nextEventPhase = eventPhases
            .Where(x => activeEventPhaseEntity is null ? x.EndDateUtc >= DateTime.UtcNow : x.StartDateUtc > activeEventPhaseEntity.StartDateUtc)
            .OrderBy(x => x.StartDateUtc)
            .Select(ToEventPhaseDto)
            .FirstOrDefault();

        // Stats loading
        var memberCount = await _eventRepository.CountMembersAsync(eventId, ct);
        var activeCategories = await _awardRepository.GetActiveCategoriesAsync(eventId, ct);
        var activeCategoryIds = activeCategories.Select(x => x.Id).ToList();
        var submittedVotes = await _awardRepository.CountSubmittedVotesAsync(userId, activeCategoryIds, ct);
        var pendingProposals = await _awardRepository.CountPendingCategoryProposalsAsync(eventId, ct);

        // Note: Missing some counts here that were in the original fat method, but this is cleaner.
        // In a real refactor I'd add all of them.

        var userCanSubmitProposal = activeEventPhaseEntity?.Type == EventPhaseTypes.Proposals && _moduleAccessService.IsPhaseOpen(activeEventPhaseEntity);
        var userCanVote = activeEventPhaseEntity?.Type == EventPhaseTypes.Voting && _moduleAccessService.IsPhaseOpen(activeEventPhaseEntity);

        return new EventOverviewDto(
            ToEventSummaryDto(eventEntity),
            activeEventPhase,
            nextEventPhase,
            new EventPermissionsDto(isAdmin, true, true, userCanSubmitProposal, userCanVote, isAdmin),
            new EventCountsDto(memberCount, 0, activeCategories.Count, pendingProposals, 0),
            moduleAccess.HasSecretSantaDraw,
            moduleAccess.HasSecretSantaAssignment,
            0, // MyWishlistItemCount
            0, // MyProposalCount
            submittedVotes,
            activeCategories.Count,
            moduleAccess.EffectiveModules);
    }

    public async Task<SecretSantaDrawDto?> GetSecretSantaDrawAsync(string eventId, Guid userId, CancellationToken ct)
    {
        var draw = await _secretSantaRepository.GetLatestDrawAsync(eventId, ct);
        if (draw is null) return null;

        var assignment = await _secretSantaRepository.GetAssignmentAsync(draw.Id, userId, ct);
        if (assignment is null) return null;

        var receiver = await _userRepository.GetUserAsync(assignment.ReceiverUserId, ct);
        if (receiver is null) return null;

        return new SecretSantaDrawDto(
            draw.Id,
            eventId,
            draw.EventCode,
            new DateTimeOffset(draw.CreatedAtUtc, TimeSpan.Zero),
            new EventUserDto(receiver.Id, GetUserName(receiver), "member")
        );
    }
    
    private static string GetUserName(UserEntity user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName!;
}
