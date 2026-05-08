using Canhoes.Api.Models;
using Canhoes.Api.DTOs;

namespace Canhoes.Api.Access;

public interface IModuleAccessService
{
    Task<EventModuleAccessSnapshot> EvaluateAsync(
        string eventId,
        Guid userId,
        bool isAdmin,
        CancellationToken ct);

    Task<EventModuleAccessSnapshot> EvaluateAsync(
        string eventId,
        Guid userId,
        bool isAdmin,
        List<EventPhaseEntity> phases,
        CancellationToken ct);

    bool IsModuleEnabled(EventModulesDto effectiveModules, EventModuleKey moduleKey);
    bool IsPhaseOpen(EventPhaseEntity? eventPhase);
}
