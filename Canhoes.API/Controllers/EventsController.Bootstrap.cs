using Canhoes.Api.Access;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Canhoes.Api.Controllers.EventsControllerMappers;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    [HttpGet("active/context")]
    public async Task<ActionResult<EventActiveContextDto>> GetActiveEventContext(CancellationToken ct)
    {
        var activeEvent = await _db.Events
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .FirstOrDefaultAsync(ct);

        if (activeEvent is null) return NotFound();

        var (access, error) = await RequireEventAccessAsync(activeEvent.Id, ct);
        if (error is not null) return error;

        var phases = await LoadEventPhasesAsync(activeEvent.Id, ct);
        var activePhaseEntity = phases.FirstOrDefault(x => x.IsActive);
        var activePhase = activePhaseEntity is null ? null : ToEventPhaseDto(activePhaseEntity);
        var nextPhase = phases
            .Where(x => activePhaseEntity is null ? x.EndDateUtc >= DateTime.UtcNow : x.StartDateUtc > activePhaseEntity.StartDateUtc)
            .OrderBy(x => x.StartDateUtc)
            .Select(ToEventPhaseDto)
            .FirstOrDefault();

        var overviewDto = new EventOverviewDto(
            ToEventSummaryDto(activeEvent),
            activePhase,
            nextPhase,
            new EventPermissionsDto(access.IsAdmin, access.IsMember, access.CanAccess, false, false, access.CanManage),
            new EventCountsDto(0, 0, 0, 0, 0),
            false,
            false,
            0,
            0,
            0,
            0,
            access.IsAdmin
                ? new EventModulesDto(true, true, true, true, true, true, true, true, true, true)
                : new EventModulesDto(true, false, true, false, false, false, false, false, false, false)
        );

        return Ok(new EventActiveContextDto(ToEventSummaryDto(activeEvent), overviewDto));
    }

    [HttpGet("{eventId}/home-snapshot")]
    public async Task<ActionResult<EventHomeSnapshotDto>> GetEventHomeSnapshot([FromRoute] string eventId, CancellationToken ct)
    {
        var (access, error) = await RequireEventAccessAsync(eventId, ct);
        if (error is not null) return error;

        var overview = await GetEventOverview(eventId, ct);
        if (overview.Result is not OkObjectResult overviewOk || overviewOk.Value is not EventOverviewDto overviewDto)
        {
            return overview.Result ?? NotFound();
        }

        var voting = await GetVotingOverview(eventId, ct);
        if (voting.Result is not OkObjectResult votingOk || votingOk.Value is not EventVotingOverviewDto votingDto)
        {
            return voting.Result ?? NotFound();
        }

        var secretSanta = await GetSecretSantaOverview(eventId, ct);
        if (secretSanta.Result is not OkObjectResult santaOk || santaOk.Value is not EventSecretSantaOverviewDto secretSantaDto)
        {
            return secretSanta.Result ?? NotFound();
        }

        var recentPosts = await _db.HubPosts
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(3)
            .ToListAsync(ct);

        var postIds = recentPosts.Select(x => x.Id).ToList();
        var postDtos = recentPosts.Count == 0
            ? []
            : await BuildFeedPostDtosAsync(eventId, access.UserId, postIds, ct, recentPosts);

        return Ok(new EventHomeSnapshotDto(
            ToEventSummaryDto(access.Event),
            overviewDto,
            votingDto,
            secretSantaDto,
            postDtos));
    }
}
