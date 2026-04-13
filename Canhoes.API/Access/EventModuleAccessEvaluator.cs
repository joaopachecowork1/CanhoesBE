using System.Text.Json;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
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
        CanhoesDbContext db,
        string eventId,
        Guid userId,
        bool isAdmin,
        CancellationToken ct)
    {
        var state = await GetOrCreateEventStateAsync(db, eventId, ct);
        var activePhase = await db.EventPhases
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.IsActive)
            .OrderByDescending(x => x.StartDateUtc)
            .FirstOrDefaultAsync(ct);

        return await BuildSnapshotAsync(db, eventId, userId, isAdmin, state, activePhase, ct);
    }

    /// <summary>
    /// Overload that reuses already-loaded phases to avoid a duplicate DB query.
    /// </summary>
    public static async Task<EventModuleAccessSnapshot> EvaluateAsync(
        CanhoesDbContext db,
        string eventId,
        Guid userId,
        bool isAdmin,
        List<EventPhaseEntity> phases,
        CancellationToken ct)
    {
        var state = await GetOrCreateEventStateAsync(db, eventId, ct);
        var activePhase = phases.FirstOrDefault(x => x.IsActive);

        return await BuildSnapshotAsync(db, eventId, userId, isAdmin, state, activePhase, ct);
    }

    private static async Task<EventModuleAccessSnapshot> BuildSnapshotAsync(
        CanhoesDbContext db,
        string eventId,
        Guid userId,
        bool isAdmin,
        CanhoesEventStateEntity state,
        EventPhaseEntity? activePhase,
        CancellationToken ct)
    {
        var latestDraw = await GetLatestSecretSantaDrawAsync(db, eventId, ct);
        var hasAssignment = latestDraw is not null
            && userId != Guid.Empty
            && await db.SecretSantaAssignments
                .AsNoTracking()
                .AnyAsync(x => x.DrawId == latestDraw.Id && x.GiverUserId == userId, ct);

        var moduleVisibility = ParseModuleVisibility(state);
        var effectiveModules = BuildModuleVisibility(
            activePhase,
            latestDraw is not null,
            hasAssignment,
            state,
            moduleVisibility,
            isAdmin);

        return new EventModuleAccessSnapshot(
            eventId,
            activePhase,
            state,
            moduleVisibility,
            effectiveModules,
            latestDraw is not null,
            hasAssignment);
    }

    public static async Task<CanhoesEventStateEntity> GetOrCreateEventStateAsync(
        CanhoesDbContext db,
        string eventId,
        CancellationToken ct)
    {
        var existingState = await db.CanhoesEventState
            .FirstOrDefaultAsync(x => x.EventId == eventId, ct);
        if (existingState is not null) return existingState;

        // Optimistic insert with retry on conflict — avoids race condition
        // when multiple requests try to create state simultaneously.
        const int maxRetries = 3;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            existingState = await db.CanhoesEventState
                .FirstOrDefaultAsync(x => x.EventId == eventId, ct);
            if (existingState is not null) return existingState;

            var nextId = (await db.CanhoesEventState
                .AsNoTracking()
                .MaxAsync(x => (int?)x.Id, ct) ?? 0) + 1;
            var defaultVisibilityJson = SerializeModuleVisibility(DefaultModuleVisibility());

            var newState = new CanhoesEventStateEntity
            {
                Id = nextId,
                EventId = eventId,
                Phase = LegacyPhaseNames.Nominations,
                NominationsVisible = true,
                ResultsVisible = false,
                ModuleVisibilityJson = defaultVisibilityJson
            };

            try
            {
                db.CanhoesEventState.Add(newState);
                await db.SaveChangesAsync(ct);
                return newState;
            }
            catch (DbUpdateException)
            {
                // Another request may have inserted concurrently.
                // Rollback the current change tracker and retry.
                db.ChangeTracker.Clear();
            }
        }

        // Final fallback — one more read after retries exhausted
        existingState = await db.CanhoesEventState
            .FirstOrDefaultAsync(x => x.EventId == eventId, ct);
        if (existingState is not null) return existingState;

        throw new InvalidOperationException(
            $"Could not create event state for '{eventId}' after {maxRetries} retries.");
    }

    public static EventAdminModuleVisibilityDto ParseModuleVisibility(CanhoesEventStateEntity? state)
    {
        if (string.IsNullOrWhiteSpace(state?.ModuleVisibilityJson))
        {
            return DefaultModuleVisibility();
        }

        try
        {
            return JsonSerializer.Deserialize<EventAdminModuleVisibilityDto>(state.ModuleVisibilityJson)
                ?? DefaultModuleVisibility();
        }
        catch
        {
            return DefaultModuleVisibility();
        }
    }

    public static string SerializeModuleVisibility(EventAdminModuleVisibilityDto visibility) =>
        JsonSerializer.Serialize(visibility);

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

    public static void ApplyLegacyStateForPhase(CanhoesEventStateEntity state, string phaseType)
    {
        switch (phaseType)
        {
            case EventPhaseTypes.Draw:
                state.Phase = LegacyPhaseNames.Locked;
                state.NominationsVisible = false;
                state.ResultsVisible = false;
                break;
            case EventPhaseTypes.Proposals:
                state.Phase = LegacyPhaseNames.Nominations;
                state.NominationsVisible = true;
                state.ResultsVisible = false;
                break;
            case EventPhaseTypes.Voting:
                state.Phase = LegacyPhaseNames.Voting;
                state.NominationsVisible = false;
                state.ResultsVisible = false;
                break;
            case EventPhaseTypes.Results:
                state.Phase = LegacyPhaseNames.Gala;
                state.NominationsVisible = false;
                state.ResultsVisible = true;
                break;
        }
    }

    public static EventModulesDto BuildModuleVisibility(
        EventPhaseEntity? activePhase,
        bool hasSecretSantaDraw,
        bool hasSecretSantaAssignment,
        CanhoesEventStateEntity? state,
        EventAdminModuleVisibilityDto moduleVisibility,
        bool isAdmin)
    {
        var legacyPhase = state?.Phase?.Trim().ToLowerInvariant();
        var nominationsVisible = state?.NominationsVisible ?? false;
        var resultsVisible = state?.ResultsVisible ?? false;
        var activeType = activePhase?.Type;

        var isDrawPhase = activeType == EventPhaseTypes.Draw;
        var isProposalPhase = activeType == EventPhaseTypes.Proposals || legacyPhase == LegacyPhaseNames.Nominations;
        var isVotingPhase = activeType == EventPhaseTypes.Voting || legacyPhase == LegacyPhaseNames.Voting;
        var isResultsPhase =
            activeType == EventPhaseTypes.Results ||
            legacyPhase == LegacyPhaseNames.Gala ||
            (legacyPhase == LegacyPhaseNames.Locked && resultsVisible);

        var proposalModulesVisible = isAdmin || (nominationsVisible && isProposalPhase);
        var resultsModulesVisible = isAdmin || resultsVisible || isResultsPhase;
        var baseSecretSantaVisible = isDrawPhase || hasSecretSantaDraw || hasSecretSantaAssignment || isVotingPhase || isResultsPhase;
        var baseCategoriesVisible = isProposalPhase || isVotingPhase || resultsModulesVisible;
        var baseVotingVisible = isVotingPhase;
        var baseNomineesVisible = nominationsVisible || resultsModulesVisible;

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
            SecretSanta: moduleVisibility.SecretSanta && baseSecretSantaVisible,
            Wishlist: moduleVisibility.Wishlist,
            Categories: moduleVisibility.Categories && baseCategoriesVisible,
            Voting: moduleVisibility.Voting && baseVotingVisible,
            Gala: moduleVisibility.Gala && resultsModulesVisible,
            Stickers: moduleVisibility.Stickers && proposalModulesVisible,
            Measures: moduleVisibility.Measures && proposalModulesVisible,
            Nominees: moduleVisibility.Nominees && baseNomineesVisible,
            Admin: false
        );
    }

    public static bool IsModuleEnabled(EventModulesDto modules, EventModuleKey moduleKey)
    {
        return moduleKey switch
        {
            EventModuleKey.Feed => modules.Feed,
            EventModuleKey.SecretSanta => modules.SecretSanta,
            EventModuleKey.Wishlist => modules.Wishlist,
            EventModuleKey.Categories => modules.Categories,
            EventModuleKey.Voting => modules.Voting,
            EventModuleKey.Gala => modules.Gala,
            EventModuleKey.Stickers => modules.Stickers,
            EventModuleKey.Measures => modules.Measures,
            EventModuleKey.Nominees => modules.Nominees,
            EventModuleKey.Admin => modules.Admin,
            _ => false
        };
    }

    public static bool IsPhaseOpen(EventPhaseEntity? phase)
    {
        if (phase is null || !phase.IsActive) return false;

        var now = DateTime.UtcNow;
        return phase.StartDateUtc <= now && now <= phase.EndDateUtc;
    }

    private static Task<SecretSantaDrawEntity?> GetLatestSecretSantaDrawAsync(
        CanhoesDbContext db,
        string eventId,
        CancellationToken ct)
    {
        var eventCodes = GetSecretSantaEventCodeCandidates(eventId);
        return db.SecretSantaDraws
            .AsNoTracking()
            .Where(x => eventCodes.Contains(x.EventCode))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private static List<string> GetSecretSantaEventCodeCandidates(string eventId)
    {
        var codes = new List<string> { eventId };

        if (string.Equals(eventId, EventContextDefaults.DefaultEventId, StringComparison.OrdinalIgnoreCase))
        {
            var legacyYearCode = $"canhoes{DateTime.UtcNow.Year}";
            if (!codes.Contains(legacyYearCode, StringComparer.OrdinalIgnoreCase))
            {
                codes.Add(legacyYearCode);
            }
        }

        return codes;
    }

}
