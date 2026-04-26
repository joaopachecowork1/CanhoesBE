using Canhoes.Api.Access;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Canhoes.Api.Services;
using Microsoft.AspNetCore.Authorization;
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
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Canhoes.Api.Hubs.EventHub> _hub;

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

    public EventsController(
        CanhoesDbContext db, 
        IWebHostEnvironment? env = null, 
        SecretSantaService? secretSanta = null, 
        IMemoryCache? cache = null,
        Microsoft.AspNetCore.SignalR.IHubContext<Canhoes.Api.Hubs.EventHub>? hub = null)
    {
        _db = db;
        _env = env;
        _secretSanta = secretSanta!;
        _cache = cache!;
        _hub = hub!;
    }

    /// <summary>
    /// Lists all events, ordering by the currently active event first, then alphabetically.
    /// Results are cached for 60 seconds.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of event summaries.</returns>
    [HttpGet]
    public async Task<ActionResult<List<EventSummaryDto>>> ListEvents(CancellationToken ct)
    {
        const string cacheKey = "EventsList";
        if (_cache.TryGetValue(cacheKey, out List<EventSummaryDto>? cached))
        {
            return Ok(cached);
        }

        var eventSummaries = await LoadEventSummariesAsync(ct);
        _cache.Set(cacheKey, eventSummaries, TimeSpan.FromSeconds(60));

        return Ok(eventSummaries);
    }

    /// <summary>
    /// Retrieves the full context for a specific event, including members and all defined phases.
    /// Requires membership or admin access. Results are cached for 30 seconds.
    /// </summary>
    /// <param name="eventId">The unique event identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The event context containing summaries, members, and phases.</returns>
    [HttpGet("{eventId}")]
    public async Task<ActionResult<EventContextDto>> GetEventContext(
        [FromRoute] string eventId,
        CancellationToken ct)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct);
        if (accessError is not null) return accessError;

        var cacheKey = $"EventContext_{eventId}";
        if (_cache.TryGetValue(cacheKey, out EventContextDto? cached))
        {
            return Ok(cached);
        }

        var memberDirectory = await LoadEventMemberDirectoryAsync(eventId, ct);
        var members = memberDirectory.Members;
        var userLookup = memberDirectory.UsersById;

        var eventUserDtos = members
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

        var eventPhaseDtos = (await LoadEventPhasesAsync(eventId, ct))
            .Select(ToEventPhaseDto)
            .ToList();

        var activeEventPhase = eventPhaseDtos.FirstOrDefault(x => x.IsActive);

        var eventContext = new EventContextDto(
            ToEventSummaryDto(eventAccess.Event),
            eventUserDtos,
            eventPhaseDtos,
            activeEventPhase
        );

        _cache.Set(cacheKey, eventContext, TimeSpan.FromSeconds(30));

        return Ok(eventContext);
    }

    /// <summary>
    /// Returns a high-level overview of the event for the member dashboard.
    /// Optimized to consolidate counts and module visibility in a single response.
    /// Results are cached for 30 seconds.
    /// </summary>
    /// <param name="eventId">The unique event identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An overview containing permissions, counts, and active phase information.</returns>
    [HttpGet("{eventId}/overview")]
    public async Task<ActionResult<EventOverviewDto>> GetEventOverview(
        [FromRoute] string eventId,
        CancellationToken ct)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct);
        if (accessError is not null) return accessError;

        var cacheKey = $"EventOverview_{eventId}_{eventAccess.UserId}";
        if (_cache.TryGetValue(cacheKey, out EventOverviewDto? cached))
        {
            return Ok(cached);
        }

        var eventPhases = await LoadEventPhasesAsync(eventId, ct);
        var moduleAccess = await EventModuleAccessEvaluator.EvaluateAsync(_db, eventId, eventAccess.UserId, eventAccess.IsAdmin, eventPhases, ct);
        
        var activeEventPhaseEntity = moduleAccess.ActivePhase ?? eventPhases.FirstOrDefault(x => x.IsActive);
        var activeEventPhase = activeEventPhaseEntity is null ? null : ToEventPhaseDto(activeEventPhaseEntity);
        var nextEventPhase = eventPhases
            .Where(x => activeEventPhaseEntity is null ? x.EndDateUtc >= DateTime.UtcNow : x.StartDateUtc > activeEventPhaseEntity.StartDateUtc)
            .OrderBy(x => x.StartDateUtc)
            .Select(ToEventPhaseDto)
            .FirstOrDefault();

        // Optimized consolidated projection
        var eventStats = await _db.Events
            .AsNoTracking()
            .Where(e => e.Id == eventId)
            .Select(e => new
            {
                ActiveCategoryCount = _db.AwardCategories.Count(c => c.EventId == eventId && c.IsActive),
                SubmittedVotes = _db.Votes.Count(v => v.UserId == eventAccess.UserId && _db.AwardCategories.Any(c => c.Id == v.CategoryId && c.EventId == eventId && c.IsActive))
                    + _db.UserVotes.Count(uv => uv.VoterUserId == eventAccess.UserId && _db.AwardCategories.Any(c => c.Id == uv.CategoryId && c.EventId == eventId && c.IsActive)),
                MemberCount = _db.EventMembers.Count(m => m.EventId == eventId),
                PostCount = _db.HubPosts.Count(p => p.EventId == eventId),
                PendingProposals = _db.CategoryProposals.Count(cp => cp.EventId == eventId && cp.Status == ProposalStatus.Pending),
                UserWishlistCount = _db.WishlistItems.Count(w => w.EventId == eventId && w.UserId == eventAccess.UserId),
                UserProposalCount = _db.CategoryProposals.Count(cp => cp.EventId == eventId && cp.ProposedByUserId == eventAccess.UserId)
            })
            .FirstOrDefaultAsync(ct);

        if (eventStats == null) return NotFound();

        var userCanSubmitProposal = activeEventPhaseEntity?.Type == EventPhaseTypes.Proposals && IsPhaseOpen(activeEventPhaseEntity);
        var userCanVote = activeEventPhaseEntity?.Type == EventPhaseTypes.Voting && IsPhaseOpen(activeEventPhaseEntity);

        var eventOverview = new EventOverviewDto(
            ToEventSummaryDto(eventAccess.Event),
            activeEventPhase,
            nextEventPhase,
            new EventPermissionsDto(eventAccess.IsAdmin, eventAccess.IsMember, eventAccess.CanAccess, userCanSubmitProposal, userCanVote, eventAccess.CanManage),
            new EventCountsDto(eventStats.MemberCount, eventStats.PostCount, eventStats.ActiveCategoryCount, eventStats.PendingProposals, eventStats.UserWishlistCount),
            moduleAccess.HasSecretSantaDraw,
            moduleAccess.HasSecretSantaAssignment,
            eventStats.UserWishlistCount,
            eventStats.UserProposalCount,
            eventStats.SubmittedVotes,
            eventStats.ActiveCategoryCount,
            moduleAccess.EffectiveModules);

        _cache.Set(cacheKey, eventOverview, TimeSpan.FromSeconds(30));

        return Ok(eventOverview);
    }
}
