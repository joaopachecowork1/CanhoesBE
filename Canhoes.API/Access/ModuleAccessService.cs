using System.Text.Json;
using Canhoes.Api.Models;
using Canhoes.Api.DTOs;
using Canhoes.Api.Repositories;

namespace Canhoes.Api.Access;

public sealed class ModuleAccessService : IModuleAccessService
{
    private readonly IEventRepository _eventRepository;
    private readonly ISecretSantaRepository _secretSantaRepository;

    public ModuleAccessService(
        IEventRepository eventRepository,
        ISecretSantaRepository secretSantaRepository)
    {
        _eventRepository = eventRepository;
        _secretSantaRepository = secretSantaRepository;
    }

    public async Task<EventModuleAccessSnapshot> EvaluateAsync(
        string eventId,
        Guid userId,
        bool isAdmin,
        CancellationToken ct)
    {
        var eventState = await GetOrCreateEventStateAsync(eventId, ct);
        var activePhase = await _eventRepository.GetActivePhaseAsync(eventId, null!, ct); // phaseType null gets any active phase

        return await BuildSnapshotAsync(eventId, userId, isAdmin, eventState, activePhase, ct);
    }

    public async Task<EventModuleAccessSnapshot> EvaluateAsync(
        string eventId,
        Guid userId,
        bool isAdmin,
        List<EventPhaseEntity> phases,
        CancellationToken ct)
    {
        var eventState = await GetOrCreateEventStateAsync(eventId, ct);
        var activePhase = phases.FirstOrDefault(x => x.IsActive);

        return await BuildSnapshotAsync(eventId, userId, isAdmin, eventState, activePhase, ct);
    }

    private async Task<EventModuleAccessSnapshot> BuildSnapshotAsync(
        string eventId,
        Guid userId,
        bool isAdmin,
        CanhoesEventStateEntity eventState,
        EventPhaseEntity? activePhase,
        CancellationToken ct)
    {
        var latestSecretSantaDraw = await _secretSantaRepository.GetLatestDrawAsync(eventId, ct);
        var hasSecretSantaAssignment = latestSecretSantaDraw is not null
            && userId != Guid.Empty
            && (await _secretSantaRepository.GetAssignmentAsync(latestSecretSantaDraw.Id, userId, ct)) is not null;

        var moduleVisibility = ParseModuleVisibility(eventState);
        var effectiveModules = BuildModuleVisibility(
            activePhase,
            latestSecretSantaDraw is not null,
            hasSecretSantaAssignment,
            eventState,
            moduleVisibility,
            isAdmin);

        return new EventModuleAccessSnapshot(
            eventId,
            activePhase,
            eventState,
            moduleVisibility,
            effectiveModules,
            latestSecretSantaDraw is not null,
            hasSecretSantaAssignment);
    }

    private async Task<CanhoesEventStateEntity> GetOrCreateEventStateAsync(
        string eventId,
        CancellationToken ct)
    {
        var existingEventState = await _eventRepository.GetEventStateAsync(eventId, ct);
        if (existingEventState is not null) return existingEventState;

        // Simplified creation logic for the service. 
        // In a real scenario, we might want to keep the retry logic or move it to the repository.
        var newEventState = new CanhoesEventStateEntity
        {
            Id = 0, // Should be handled by DB or repo
            EventId = eventId,
            Phase = LegacyPhaseNames.Nominations,
            NominationsVisible = true,
            ResultsVisible = false,
            ModuleVisibilityJson = JsonSerializer.Serialize(DefaultModuleVisibility())
        };

        await _eventRepository.AddEventStateAsync(newEventState, ct);
        await _eventRepository.SaveChangesAsync(ct);
        return newEventState;
    }

    public EventAdminModuleVisibilityDto ParseModuleVisibility(CanhoesEventStateEntity? eventState)
    {
        if (string.IsNullOrWhiteSpace(eventState?.ModuleVisibilityJson))
        {
            return DefaultModuleVisibility();
        }

        try
        {
            return JsonSerializer.Deserialize<EventAdminModuleVisibilityDto>(eventState.ModuleVisibilityJson)
                ?? DefaultModuleVisibility();
        }
        catch
        {
            return DefaultModuleVisibility();
        }
    }

    public EventAdminModuleVisibilityDto DefaultModuleVisibility() =>
        new(
            Feed: true,
            SecretSanta: true,
            Wishlist: true,
            Categories: true,
            Voting: true,
            Gala: true,
            Stickers: true,
            Measures: true,
            Nominees: true
        );

    private EventModulesDto BuildModuleVisibility(
        EventPhaseEntity? activePhase,
        bool hasSecretSantaDraw,
        bool hasSecretSantaAssignment,
        CanhoesEventStateEntity? eventState,
        EventAdminModuleVisibilityDto moduleVisibility,
        bool isAdmin)
    {
        var legacyPhaseName = eventState?.Phase?.Trim().ToLowerInvariant();
        var isNominationsVisible = eventState?.NominationsVisible ?? false;
        var isResultsVisible = eventState?.ResultsVisible ?? false;
        var activePhaseType = activePhase?.Type;

        var isDrawPhase = activePhaseType == EventPhaseTypes.Draw;
        var isProposalPhase = activePhaseType == EventPhaseTypes.Proposals || legacyPhaseName == LegacyPhaseNames.Nominations;
        var isVotingPhase = activePhaseType == EventPhaseTypes.Voting || legacyPhaseName == LegacyPhaseNames.Voting;
        var isResultsPhase =
            activePhaseType == EventPhaseTypes.Results ||
            legacyPhaseName == LegacyPhaseNames.Gala ||
            (legacyPhaseName == LegacyPhaseNames.Locked && isResultsVisible);

        var isProposalModulesVisible = isAdmin || (isNominationsVisible && isProposalPhase);
        var isResultsModulesVisible = isAdmin || isResultsVisible || isResultsPhase;
        var isBaseSecretSantaVisible = isDrawPhase || hasSecretSantaDraw || hasSecretSantaAssignment || isVotingPhase || isResultsPhase;
        var isBaseCategoriesVisible = isProposalPhase || isVotingPhase || isResultsModulesVisible;
        var isBaseVotingVisible = isVotingPhase;
        var isBaseNomineesVisible = isNominationsVisible || isResultsModulesVisible;

        if (isAdmin)
        {
            return new EventModulesDto(
                Feed: true,
                SecretSanta: true,
                Wishlist: true,
                Categories: true,
                Voting: true,
                Gala: true,
                Stickers: true,
                Measures: true,
                Nominees: true,
                Admin: true
            );
        }

        return new EventModulesDto(
            Feed: moduleVisibility.Feed,
            SecretSanta: moduleVisibility.SecretSanta && isBaseSecretSantaVisible,
            Wishlist: moduleVisibility.Wishlist,
            Categories: moduleVisibility.Categories && isBaseCategoriesVisible,
            Voting: moduleVisibility.Voting && isBaseVotingVisible,
            Gala: moduleVisibility.Gala && isResultsModulesVisible,
            Stickers: moduleVisibility.Stickers && isProposalModulesVisible,
            Measures: moduleVisibility.Measures && isProposalModulesVisible,
            Nominees: moduleVisibility.Nominees && isBaseNomineesVisible,
            Admin: false
        );
    }

    public bool IsModuleEnabled(EventModulesDto effectiveModules, EventModuleKey moduleKey)
    {
        return moduleKey switch
        {
            EventModuleKey.Feed => effectiveModules.Feed,
            EventModuleKey.SecretSanta => effectiveModules.SecretSanta,
            EventModuleKey.Wishlist => effectiveModules.Wishlist,
            EventModuleKey.Categories => effectiveModules.Categories,
            EventModuleKey.Voting => effectiveModules.Voting,
            EventModuleKey.Gala => effectiveModules.Gala,
            EventModuleKey.Stickers => effectiveModules.Stickers,
            EventModuleKey.Measures => effectiveModules.Measures,
            EventModuleKey.Nominees => effectiveModules.Nominees,
            EventModuleKey.Admin => effectiveModules.Admin,
            _ => false
        };
    }

    public bool IsPhaseOpen(EventPhaseEntity? eventPhase)
    {
        if (eventPhase is null || !eventPhase.IsActive) return false;

        var currentTimeUtc = DateTime.UtcNow;
        return eventPhase.StartDateUtc <= currentTimeUtc && currentTimeUtc <= eventPhase.EndDateUtc;
    }
}
