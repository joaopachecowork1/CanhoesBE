using System.Text.Json;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Canhoes.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Access;

internal enum EventModuleKey
{
    Feed,
    SecretSanta,
    Wishlist,
    Categories,
    Voting,
    Gala,
    Stickers,
    Measures,
    Nominees,
    Admin
}

internal sealed record EventModuleAccessSnapshot(
    string EventId,
    EventPhaseEntity? ActivePhase,
    CanhoesEventStateEntity State,
    EventAdminModuleVisibilityDto ModuleVisibility,
    EventModulesDto EffectiveModules,
    bool HasSecretSantaDraw,
    bool HasSecretSantaAssignment);

internal static class EventModuleAccessEvaluator
{
    public static async Task<EventModuleAccessSnapshot> EvaluateAsync(
        CanhoesDbContext dbContext,
        string eventId,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var eventState = await GetOrCreateEventStateAsync(dbContext, eventId, cancellationToken);
        var activePhase = await dbContext.EventPhases
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.IsActive)
            .OrderByDescending(x => x.StartDateUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return await BuildSnapshotAsync(dbContext, eventId, userId, isAdmin, eventState, activePhase, cancellationToken);
    }

    /// <summary>
    /// Overload that reuses already-loaded phases to avoid a duplicate DB query.
    /// </summary>
    public static async Task<EventModuleAccessSnapshot> EvaluateAsync(
        CanhoesDbContext dbContext,
        string eventId,
        Guid userId,
        bool isAdmin,
        List<EventPhaseEntity> phases,
        CancellationToken cancellationToken)
    {
        var eventState = await GetOrCreateEventStateAsync(dbContext, eventId, cancellationToken);
        var activePhase = phases.FirstOrDefault(x => x.IsActive);

        return await BuildSnapshotAsync(dbContext, eventId, userId, isAdmin, eventState, activePhase, cancellationToken);
    }

    private static async Task<EventModuleAccessSnapshot> BuildSnapshotAsync(
        CanhoesDbContext dbContext,
        string eventId,
        Guid userId,
        bool isAdmin,
        CanhoesEventStateEntity eventState,
        EventPhaseEntity? activePhase,
        CancellationToken cancellationToken)
    {
        var latestSecretSantaDraw = await GetLatestSecretSantaDrawAsync(dbContext, eventId, cancellationToken);
        var hasSecretSantaAssignment = latestSecretSantaDraw is not null
            && userId != Guid.Empty
            && await dbContext.SecretSantaAssignments
                .AsNoTracking()
                .AnyAsync(x => x.DrawId == latestSecretSantaDraw.Id && x.GiverUserId == userId, cancellationToken);

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

    public static async Task<CanhoesEventStateEntity> GetOrCreateEventStateAsync(
        CanhoesDbContext dbContext,
        string eventId,
        CancellationToken cancellationToken)
    {
        var existingEventState = await dbContext.CanhoesEventState
            .FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
        if (existingEventState is not null) return existingEventState;

        // Optimistic insert with retry on conflict — avoids race condition
        // when multiple requests try to create state simultaneously.
        const int maxRetries = 3;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            existingEventState = await dbContext.CanhoesEventState
                .FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
            if (existingEventState is not null) return existingEventState;

            var nextStateId = (await dbContext.CanhoesEventState
                .AsNoTracking()
                .MaxAsync(x => (int?)x.Id, cancellationToken) ?? 0) + 1;
            var defaultVisibilityJson = SerializeModuleVisibility(DefaultModuleVisibility());

            var newEventState = new CanhoesEventStateEntity
            {
                Id = nextStateId,
                EventId = eventId,
                Phase = LegacyPhaseNames.Nominations,
                NominationsVisible = true,
                ResultsVisible = false,
                ModuleVisibilityJson = defaultVisibilityJson
            };

            try
            {
                dbContext.CanhoesEventState.Add(newEventState);
                await dbContext.SaveChangesAsync(cancellationToken);
                return newEventState;
            }
            catch (DbUpdateException)
            {
                // Another request may have inserted concurrently.
                // Rollback the current change tracker and retry.
                dbContext.ChangeTracker.Clear();
            }
        }

        // Final fallback — one more read after retries exhausted
        existingEventState = await dbContext.CanhoesEventState
            .FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
        if (existingEventState is not null) return existingEventState;

        throw new InvalidOperationException(
            $"Could not create event state for '{eventId}' after {maxRetries} retries.");
    }

    public static EventAdminModuleVisibilityDto ParseModuleVisibility(CanhoesEventStateEntity? eventState)
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

    public static string SerializeModuleVisibility(EventAdminModuleVisibilityDto moduleVisibility) =>
        JsonSerializer.Serialize(moduleVisibility);

    public static EventAdminModuleVisibilityDto DefaultModuleVisibility() =>
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

    public static void ApplyLegacyStateForPhase(CanhoesEventStateEntity eventState, string phaseType)
    {
        switch (phaseType)
        {
            case EventPhaseTypes.Draw:
                eventState.Phase = LegacyPhaseNames.Locked;
                eventState.NominationsVisible = false;
                eventState.ResultsVisible = false;
                break;
            case EventPhaseTypes.Proposals:
                eventState.Phase = LegacyPhaseNames.Nominations;
                eventState.NominationsVisible = true;
                eventState.ResultsVisible = false;
                break;
            case EventPhaseTypes.Voting:
                eventState.Phase = LegacyPhaseNames.Voting;
                eventState.NominationsVisible = false;
                eventState.ResultsVisible = false;
                break;
            case EventPhaseTypes.Results:
                eventState.Phase = LegacyPhaseNames.Gala;
                eventState.NominationsVisible = false;
                eventState.ResultsVisible = true;
                break;
        }
    }

    public static EventModulesDto BuildModuleVisibility(
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

    public static bool IsModuleEnabled(EventModulesDto effectiveModules, EventModuleKey moduleKey)
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

    public static bool IsPhaseOpen(EventPhaseEntity? eventPhase)
    {
        if (eventPhase is null || !eventPhase.IsActive) return false;

        var currentTimeUtc = DateTime.UtcNow;
        return eventPhase.StartDateUtc <= currentTimeUtc && currentTimeUtc <= eventPhase.EndDateUtc;
    }

    private static Task<SecretSantaDrawEntity?> GetLatestSecretSantaDrawAsync(
        CanhoesDbContext dbContext,
        string eventId,
        CancellationToken cancellationToken)
    {
        var secretSantaCandidateCodes = GetSecretSantaEventCodeCandidates(eventId);
        return dbContext.SecretSantaDraws
            .AsNoTracking()
            .Where(x => secretSantaCandidateCodes.Contains(x.EventCode))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static List<string> GetSecretSantaEventCodeCandidates(string eventId)
    {
        var secretSantaCandidateCodes = new List<string> { eventId };

        if (string.Equals(eventId, EventContextDefaults.DefaultEventId, StringComparison.OrdinalIgnoreCase))
        {
            var legacyYearEventCode = $"canhoes{DateTime.UtcNow.Year}";
            if (!secretSantaCandidateCodes.Contains(legacyYearEventCode, StringComparer.OrdinalIgnoreCase))
            {
                secretSantaCandidateCodes.Add(legacyYearEventCode);
            }
        }

        return secretSantaCandidateCodes;
    }

}
