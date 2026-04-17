using Canhoes.Api.Access;
using Canhoes.Api.Caching;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Canhoes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using static Canhoes.Api.Controllers.EventsControllerMappers;

namespace Canhoes.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
[Authorize]
public sealed partial class EventsController : ControllerBase
{
    private readonly CanhoesDbContext _db;
    private readonly IWebHostEnvironment? _env;
    private readonly SecretSantaService _secretSanta;
    private readonly IMemoryCache _cache;

    private sealed record EventAccessContext(
        EventEntity Event,
        Guid UserId,
        bool IsAdmin,
        bool IsMember)
    {
        public bool CanAccess => IsAdmin || IsMember;
        public bool CanManage => IsAdmin;
    }

    private sealed record EventMemberDirectory(
        List<EventMemberEntity> Members,
        Dictionary<Guid, UserEntity> UsersById);

    public EventsController(CanhoesDbContext db, IWebHostEnvironment? env = null, SecretSantaService? secretSanta = null, IMemoryCache? cache = null)
    {
        _db = db;
        _env = env;
        _secretSanta = secretSanta!;
        _cache = cache!;
    }

    /// <summary>
    /// Lists events ordered by the currently active cycle first.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<EventSummaryDto>>> ListEvents(CancellationToken ct)
    {
        return Ok(await LoadEventSummariesAsync(ct));
    }

    [HttpGet("{eventId}")]
    public async Task<ActionResult<EventContextDto>> GetEventContext(
        [FromRoute] string eventId,
        CancellationToken ct)
    {
        var (access, error) = await RequireEventAccessAsync(eventId, ct);
        if (error is not null) return error;

        var directory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var members = directory.Members;
        var userLookup = directory.UsersById;

        var userDtos = members
            .Where(x => userLookup.ContainsKey(x.UserId))
            .OrderByDescending(x => x.Role == EventRoles.Admin)
            .ThenBy(x => userLookup.TryGetValue(x.UserId, out var user)
                ? GetUserName(user)
                : x.UserId.ToString())
            .Select(x =>
            {
                var user = userLookup[x.UserId];
                return new EventUserDto(user.Id, GetUserName(user), x.Role);
            })
            .ToList();

        var phaseDtos = (await LoadEventPhasesAsync(eventId, ct))
            .Select(ToEventPhaseDto)
            .ToList();

        var activePhase = phaseDtos.FirstOrDefault(x => x.IsActive);

        return Ok(new EventContextDto(
            ToEventSummaryDto(access.Event),
            userDtos,
            phaseDtos,
            activePhase
        ));
    }

    /// <summary>
    /// Returns the event shell snapshot used by the home screen and chrome to
    /// decide what the member can see and what action matters next.
    /// Optimized: phases loaded once, counts consolidated, output-cached 15s.
    /// </summary>
    [HttpGet("{eventId}/overview")]
    [ResponseCache(Duration = 15, Location = Microsoft.AspNetCore.Mvc.ResponseCacheLocation.Any)]
    public async Task<ActionResult<EventOverviewDto>> GetEventOverview(
        [FromRoute] string eventId,
        CancellationToken ct)
    {
        var (access, error) = await RequireEventAccessAsync(eventId, ct);
        if (error is not null) return error;

        // Load phases ONCE — reused by both module access and next-phase calculation
        var phases = await LoadEventPhasesAsync(eventId, ct);

        // Pass pre-loaded phases to avoid duplicate query inside EvaluateAsync
        var moduleAccess = await EventModuleAccessEvaluator.EvaluateAsync(
            _db,
            eventId,
            access.UserId,
            access.IsAdmin,
            phases,
            ct);

        var activePhaseEntity = moduleAccess.ActivePhase ?? phases.FirstOrDefault(x => x.IsActive);
        var activePhase = activePhaseEntity is null ? null : ToEventPhaseDto(activePhaseEntity);
        var nextPhase = phases
            .Where(x => activePhaseEntity is null
                ? x.EndDateUtc >= DateTime.UtcNow
                : x.StartDateUtc > activePhaseEntity.StartDateUtc)
            .OrderBy(x => x.StartDateUtc)
            .Select(ToEventPhaseDto)
            .FirstOrDefault();

        var activeCategories = await LoadActiveCategoriesAsync(eventId, ct);
        var categoryIds = activeCategories.Select(x => x.Id).ToList();
        var submittedVoteCount = await CountSubmittedVotesAsync(access.UserId, categoryIds, ct);

        // Consolidated counts — replaces 4 individual CountAsync calls
        var counts = await LoadEventCountsAsync(eventId, ct);

        // Per-user counts — still needed individually but lightweight
        var userWishlistCount = await CountWishlistItemsAsync(eventId, access.UserId, ct);
        var userProposalCount = await _db.CategoryProposals
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId && x.ProposedByUserId == access.UserId, ct);

        var canSubmitProposal = activePhaseEntity?.Type == EventPhaseTypes.Proposals
            && IsPhaseOpen(activePhaseEntity);
        var canVote = activePhaseEntity?.Type == EventPhaseTypes.Voting
            && IsPhaseOpen(activePhaseEntity);

        return Ok(new EventOverviewDto(
            ToEventSummaryDto(access.Event),
            activePhase,
            nextPhase,
            new EventPermissionsDto(
                access.IsAdmin,
                access.IsMember,
                access.CanAccess,
                canSubmitProposal,
                canVote,
                access.CanManage
            ),
            new EventCountsDto(
                counts.MemberCount,
                counts.HubPostCount,
                activeCategories.Count,
                counts.PendingCategoryProposalsCount,
                userWishlistCount
            ),
            moduleAccess.HasSecretSantaDraw,
            moduleAccess.HasSecretSantaAssignment,
            userWishlistCount,
            userProposalCount,
            submittedVoteCount,
            activeCategories.Count,
            moduleAccess.EffectiveModules
        ));
    }
}
