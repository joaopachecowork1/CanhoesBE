using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Models;
using Canhoes.Api.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    private async Task<ActionResult?> RequireManageAccessAsync(string eventId, CancellationToken ct) =>
        (await RequireEventAccessAsync(eventId, ct, requireManage: true)).Error;

    private async Task<(EventAccessContext Access, ActionResult? Error)> RequireEventAccessAsync(
        string eventId,
        CancellationToken ct,
        bool requireManage = false)
    {
        var access = await GetEventAccessAsync(eventId, ct);
        if (access is null) return (default!, NotFound());
        if (requireManage ? !access.CanManage : !access.CanAccess) return (default!, Forbid());
        return (access, null);
    }

    private async Task<(EventAccessContext Access, EventModuleAccessSnapshot Snapshot, ActionResult? Error)> RequireEventModuleAccessAsync(
        string eventId,
        EventModuleKey moduleKey,
        CancellationToken ct)
    {
        var (access, error) = await RequireEventAccessAsync(eventId, ct);
        if (error is not null) return (default!, default!, error);

        var snapshot = await EventModuleAccessEvaluator.EvaluateAsync(
            _db,
            eventId,
            access.UserId,
            access.IsAdmin,
            ct);
        if (!EventModuleAccessEvaluator.IsModuleEnabled(snapshot.EffectiveModules, moduleKey))
        {
            return (default!, default!, Forbid());
        }

        return (access, snapshot, null);
    }

    private async Task<EventAccessContext?> GetEventAccessAsync(string eventId, CancellationToken ct)
    {
        var eventEntity = await _db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eventId, ct);
        if (eventEntity is null) return null;

        var userId = HttpContext.GetUserId();
        var isAdmin = HttpContext.IsAdmin();
        var isMember = await _db.EventMembers
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId && x.UserId == userId, ct);

        return new EventAccessContext(eventEntity, userId, isAdmin, isMember);
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
        var eventCodes = GetSecretSantaEventCodeCandidates(eventId);
        return _db.SecretSantaDraws
            .AsNoTracking()
            .Where(x => eventCodes.Contains(x.EventCode))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private static bool IsPhaseOpen(EventPhaseEntity? phase) =>
        EventModuleAccessEvaluator.IsPhaseOpen(phase);

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
        var codes = new List<string> { eventId };

        if (string.Equals(
            eventId,
            EventContextDefaults.DefaultEventId,
            StringComparison.OrdinalIgnoreCase))
        {
            var legacyYearCode = $"canhoes{DateTime.UtcNow.Year}";
            if (!codes.Contains(legacyYearCode, StringComparer.OrdinalIgnoreCase))
            {
                codes.Add(legacyYearCode);
            }
        }

        return codes;
    }

    private static EventAdminModuleVisibilityDto ParseModuleVisibility(
        CanhoesEventStateEntity? legacyState) =>
        EventModuleAccessEvaluator.ParseModuleVisibility(legacyState);

    private static string SerializeModuleVisibility(
        EventAdminModuleVisibilityDto visibility) =>
        EventModuleAccessEvaluator.SerializeModuleVisibility(visibility);

    private static void ApplyLegacyStateForPhase(
        CanhoesEventStateEntity legacyState,
        string phaseType) =>
        EventModuleAccessEvaluator.ApplyLegacyStateForPhase(legacyState, phaseType);

    private static string? NormalizeProposalStatusFilter(string? status) =>
        string.IsNullOrWhiteSpace(status) ? null : NormalizeProposalStatus(status);

    private static string? NormalizeProposalStatus(string status)
    {
        var normalizedStatus = status.Trim().ToLowerInvariant();
        return ProposalStatus.IsValid(normalizedStatus) ? normalizedStatus : null;
    }

    private static string? NormalizeNomineeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;

        var normalizedStatus = status.Trim().ToLowerInvariant();
        return ProposalStatus.IsValid(normalizedStatus) ? normalizedStatus : null;
    }
}
