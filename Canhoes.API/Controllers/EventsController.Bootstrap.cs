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

        var overview = await GetEventOverview(activeEvent.Id, ct);
        if (overview.Result is not OkObjectResult ok || ok.Value is not EventOverviewDto overviewDto)
        {
            return overview.Result ?? NotFound();
        }

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
