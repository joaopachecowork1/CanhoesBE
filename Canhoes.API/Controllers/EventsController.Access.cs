using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    private async Task<ActionResult?> RequireManageAccessAsync(string eventId, CancellationToken ct) =>
        (await RequireEventAccessAsync(eventId, ct, requireManage: true)).accessError;

    private async Task<(EventAccessContext eventAccess, ActionResult? accessError)> RequireEventAccessAsync(
        string eventId,
        CancellationToken ct,
        bool requireManage = false)
    {
        var eventAccess = await GetEventAccessAsync(eventId, ct);
        if (eventAccess is null) return (default!, NotFound());
        if (requireManage ? !eventAccess.CanManage : !eventAccess.CanAccess) return (default!, Forbid());
        return (eventAccess, null);
    }

    private async Task<(EventAccessContext eventAccess, EventModuleAccessSnapshot moduleAccessSnapshot, ActionResult? accessError)> RequireEventModuleAccessAsync(
        string eventId,
        EventModuleKey moduleKey,
        CancellationToken ct)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct);
        if (accessError is not null) return (default!, default!, accessError);

        var phases = await LoadEventPhasesAsync(eventId, ct);
        var moduleAccessSnapshot = await EventModuleAccessEvaluator.EvaluateAsync(
            _db,
            eventId,
            eventAccess.UserId,
            eventAccess.IsAdmin,
            phases,
            ct);
        if (!EventModuleAccessEvaluator.IsModuleEnabled(moduleAccessSnapshot.EffectiveModules, moduleKey))
        {
            return (default!, default!, Forbid());
        }

        return (eventAccess, moduleAccessSnapshot, null);
    }

    private async Task<EventAccessContext?> GetEventAccessAsync(string eventId, CancellationToken ct)
    {
        var eventEntity = await _db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eventId, ct);
        if (eventEntity is null) return null;

        var currentUserId = HttpContext.GetUserId();
        var isCurrentUserAdmin = HttpContext.IsAdmin();
        var isCurrentUserMember = await _db.EventMembers
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId && x.UserId == currentUserId, ct);

        return new EventAccessContext(eventEntity, currentUserId, isCurrentUserAdmin, isCurrentUserMember);
    }

    private Task<EventPhaseEntity?> GetActivePhaseAsync(
        string eventId,
        string phaseType,
        CancellationToken ct) =>
        _db.EventPhases
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Type == phaseType && x.IsActive)
            .OrderByDescending(x => x.StartDateUtc)
            .FirstOrDefaultAsync(ct);

    private Task<SecretSantaDrawEntity?> GetLatestSecretSantaDrawAsync(
        string eventId,
        CancellationToken ct)
    {
        var secretSantaEventCodes = GetSecretSantaEventCodeCandidates(eventId);
        return _db.SecretSantaDraws
            .AsNoTracking()
            .Where(x => secretSantaEventCodes.Contains(x.EventCode))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private static bool IsPhaseOpen(EventPhaseEntity? eventPhase) =>
        EventModuleAccessEvaluator.IsPhaseOpen(eventPhase);

    private Task<CanhoesEventStateEntity> GetOrCreateEventStateAsync(
        string eventId,
        CancellationToken ct) =>
        EventModuleAccessEvaluator.GetOrCreateEventStateAsync(_db, eventId, ct);

    private static string NormalizeSecretSantaEventCode(
        string eventId,
        string? requestedEventCode) =>
        string.IsNullOrWhiteSpace(requestedEventCode) ? eventId : requestedEventCode.Trim();

    private static List<string> GetSecretSantaEventCodeCandidates(string eventId)
    {
        var candidateCodes = new List<string> { eventId };

        if (string.Equals(
            eventId,
            EventContextDefaults.DefaultEventId,
            StringComparison.OrdinalIgnoreCase))
        {
            var legacyYearEventCode = $"canhoes{DateTime.UtcNow.Year}";
            if (!candidateCodes.Contains(legacyYearEventCode, StringComparer.OrdinalIgnoreCase))
            {
                candidateCodes.Add(legacyYearEventCode);
            }
        }

        return candidateCodes;
    }

    private static EventAdminModuleVisibilityDto ParseModuleVisibility(
        CanhoesEventStateEntity? legacyState) =>
        EventModuleAccessEvaluator.ParseModuleVisibility(legacyState);

    private static string SerializeModuleVisibility(
        EventAdminModuleVisibilityDto moduleVisibility) =>
        EventModuleAccessEvaluator.SerializeModuleVisibility(moduleVisibility);

    private static void ApplyLegacyStateForPhase(
        CanhoesEventStateEntity legacyState,
        string phaseType) =>
        EventModuleAccessEvaluator.ApplyLegacyStateForPhase(legacyState, phaseType);

    private static string? NormalizeProposalStatusFilter(string? proposalStatus) =>
        string.IsNullOrWhiteSpace(proposalStatus) ? null : NormalizeProposalStatus(proposalStatus);

    private static string? NormalizeProposalStatus(string proposalStatus)
    {
        var normalizedProposalStatus = proposalStatus.Trim().ToLowerInvariant();
        return ProposalStatus.IsValid(normalizedProposalStatus) ? normalizedProposalStatus : null;
    }

    private static string? NormalizeNomineeStatusFilter(string? nomineeStatus)
    {
        if (string.IsNullOrWhiteSpace(nomineeStatus)) return null;

        var normalizedNomineeStatus = nomineeStatus.Trim().ToLowerInvariant();
        return ProposalStatus.IsValid(normalizedNomineeStatus) ? normalizedNomineeStatus : null;
    }
}
