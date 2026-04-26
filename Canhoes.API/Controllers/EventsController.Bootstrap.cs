using Canhoes.Api.Access;
using Canhoes.Api.Models;
using Canhoes.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Canhoes.Api.Controllers.EventsControllerMappers;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    /// <summary>
    /// Retrieves the initial context for the currently active event, including phases and basic permissions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active event context containing summary and overview information.</returns>
    [HttpGet("active/context")]
    public async Task<ActionResult<EventActiveContextDto>> GetActiveEventContext(CancellationToken ct)
    {
        var activeEventEntity = await _db.Events
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .FirstOrDefaultAsync(ct);

        if (activeEventEntity is null) return NotFound();

        var (eventUserAccess, eventAccessError) = await RequireEventAccessAsync(activeEventEntity.Id, ct);
        if (eventAccessError is not null) return eventAccessError;

        var eventPhases = await LoadEventPhasesAsync(activeEventEntity.Id, ct);
        var activeEventPhaseEntity = eventPhases.FirstOrDefault(x => x.IsActive);
        var activeEventPhaseDto = activeEventPhaseEntity is null ? null : ToEventPhaseDto(activeEventPhaseEntity);
        
        var nextEventPhaseDto = eventPhases
            .Where(x => activeEventPhaseEntity is null ? x.EndDateUtc >= DateTime.UtcNow : x.StartDateUtc > activeEventPhaseEntity.StartDateUtc)
            .OrderBy(x => x.StartDateUtc)
            .Select(ToEventPhaseDto)
            .FirstOrDefault();

        var defaultOverviewDto = new EventOverviewDto(
            ToEventSummaryDto(activeEventEntity),
            activeEventPhaseDto,
            nextEventPhaseDto,
            new EventPermissionsDto(eventUserAccess.IsAdmin, eventUserAccess.IsMember, eventUserAccess.CanAccess, false, false, eventUserAccess.CanManage),
            new EventCountsDto(0, 0, 0, 0, 0),
            false,
            false,
            0,
            0,
            0,
            0,
            eventUserAccess.IsAdmin
                ? new EventModulesDto(true, true, true, true, true, true, true, true, true, true)
                : new EventModulesDto(true, false, true, false, false, false, false, false, false, false)
        );

        return Ok(new EventActiveContextDto(ToEventSummaryDto(activeEventEntity), defaultOverviewDto));
    }

    /// <summary>
    /// Retrieves a home-screen snapshot for the currently active event.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The home snapshot including overview, voting, secret santa and recent posts.</returns>
    [HttpGet("active/home-snapshot")]
    public async Task<ActionResult<EventHomeSnapshotDto>> GetActiveEventHomeSnapshot(CancellationToken ct)
    {
        var activeEventEntity = await _db.Events
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .FirstOrDefaultAsync(ct);

        if (activeEventEntity is null) return NotFound();

        return await GetEventHomeSnapshot(activeEventEntity.Id, ct);
    }

    /// <summary>
    /// Retrieves a comprehensive snapshot of the event's current state for the home screen.
    /// Combines overview, voting progress, secret santa status and recent feed activity.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The complete home snapshot payload.</returns>
    [HttpGet("{eventId}/home-snapshot")]
    public async Task<ActionResult<EventHomeSnapshotDto>> GetEventHomeSnapshot([FromRoute] string eventId, CancellationToken ct)
    {
        var (eventUserAccess, eventAccessError) = await RequireEventAccessAsync(eventId, ct);
        if (eventAccessError is not null) return eventAccessError;

        var eventOverviewResult = await GetEventOverview(eventId, ct);
        if (eventOverviewResult.Result is not OkObjectResult overviewOk || overviewOk.Value is not EventOverviewDto eventOverviewDto)
        {
            return eventOverviewResult.Result ?? NotFound();
        }

        var eventVotingResult = await GetVotingOverview(eventId, ct);
        if (eventVotingResult.Result is not OkObjectResult votingOk || votingOk.Value is not EventVotingOverviewDto votingOverviewDto)
        {
            return eventVotingResult.Result ?? NotFound();
        }

        var eventSecretSantaResult = await GetSecretSantaOverview(eventId, ct);
        if (eventSecretSantaResult.Result is not OkObjectResult santaOk || santaOk.Value is not EventSecretSantaOverviewDto secretSantaOverviewDto)
        {
            return eventSecretSantaResult.Result ?? NotFound();
        }

        var recentPostsEntities = await _db.HubPosts
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(3)
            .ToListAsync(ct);

        var recentPostIds = recentPostsEntities.Select(x => x.Id).ToList();
        var recentPostDtos = recentPostsEntities.Count == 0
            ? []
            : await BuildFeedPostDtosAsync(eventId, eventUserAccess.UserId, recentPostIds, ct, recentPostsEntities);

        return Ok(new EventHomeSnapshotDto(
            ToEventSummaryDto(eventUserAccess.Event),
            eventOverviewDto,
            votingOverviewDto,
            secretSantaOverviewDto,
            recentPostDtos));
    }
}
