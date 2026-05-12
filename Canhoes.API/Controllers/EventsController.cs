using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Canhoes.Api.Services;
using Canhoes.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Hosting;
using static Canhoes.Api.Mappers.EventMappers;

namespace Canhoes.Api.Controllers;

[ApiController]
[Authorize]
public sealed class EventsController : EventControllerBase
{
    private readonly IEventService _eventService;
    private readonly IAwardService _awardService;
    private readonly ISecretSantaService _secretSanta;

    public EventsController(
        IEventService eventService,
        IAwardService awardService,
        ISecretSantaService secretSanta,
        CanhoesDbContext db, 
        IWebHostEnvironment? env = null, 
        IMemoryCache? cache = null,
        Microsoft.AspNetCore.SignalR.IHubContext<Canhoes.Api.Hubs.EventHub>? hub = null) 
        : base(db, cache, hub, env)
    {
        _eventService = eventService;
        _awardService = awardService;
        _secretSanta = secretSanta;
    }

    /// <summary>
    /// Lists all events, ordering by the currently active event first, then alphabetically.
    /// Results are cached for 60 seconds.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of event summaries.</returns>
    [HttpGet]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client)]
    public async Task<ActionResult<List<EventSummaryDto>>> ListEvents(CancellationToken ct)
    {
        const string cacheKey = "EventsList";
        if (_cache.TryGetValue(cacheKey, out List<EventSummaryDto>? cached))
        {
            return Ok(cached);
        }

        var eventSummaries = await _eventService.GetEventSummariesAsync(ct);
        _cache.Set(cacheKey, eventSummaries, TimeSpan.FromSeconds(60));

        return Ok(eventSummaries);
    }

    /// <summary>
    /// Retrieves the context for the currently active event.
    /// </summary>
    [HttpGet("active/context")]
    public async Task<ActionResult<EventActiveContextDto>> GetActiveEventContext(CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();
        var isAdmin = HttpContext.IsAdmin();
        var context = await _eventService.GetActiveEventContextAsync(userId, isAdmin, ct);
        return context is null ? NotFound() : Ok(context);
    }

    /// <summary>
    /// Retrieves a snapshot of the active event for the home dashboard.
    /// </summary>
    [HttpGet("active/home-snapshot")]
    public async Task<ActionResult<EventHomeSnapshotDto>> GetActiveHomeSnapshot(CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();
        var isAdmin = HttpContext.IsAdmin();
        var snapshot = await _eventService.GetActiveHomeSnapshotAsync(userId, isAdmin, ct);
        return snapshot is null ? NotFound() : Ok(snapshot);
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

        var eventContext = await _eventService.GetEventContextAsync(eventId, eventAccess.UserId, eventAccess.IsAdmin, ct);
        if (eventContext is null) return NotFound();

        _cache.Set(cacheKey, eventContext, TimeSpan.FromSeconds(30));

        return Ok(eventContext);
    }

    /// <summary>
    /// Retrieves the high-level overview for a specific event.
    /// Requires membership or admin access. Results are cached for 30 seconds.
    /// </summary>
    /// <param name="eventId">The unique event identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The event overview containing phases, permissions, and counts.</returns>
    [HttpGet("{eventId}/overview")]
    public async Task<ActionResult<EventOverviewDto>> GetEventOverview(
        [FromRoute] string eventId,
        CancellationToken ct)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct);
        if (accessError is not null) return accessError;

        var overview = await _eventService.GetEventOverviewAsync(eventId, eventAccess.UserId, eventAccess.IsAdmin, ct);
        if (overview is null) return NotFound();

        return Ok(overview);
    }

    /// <summary>
    /// Retrieves the Secret Santa draw for the current user in the specified event.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Secret Santa draw details if found; otherwise, Not Found.</returns>
    [HttpGet("{eventId}/secret-santa/draw")]
    public async Task<ActionResult<SecretSantaDrawDto>> GetSecretSantaDraw([FromRoute] string eventId, CancellationToken ct)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct);
        if (accessError is not null) return accessError;

        var draw = await _eventService.GetSecretSantaDrawAsync(eventId, eventAccess.UserId, ct);
        if (draw is null) return NotFound("Secret Santa draw not found for you in this event.");

        return Ok(draw);
    }

    /// <summary>
    /// Retrieves a paged list of active award categories for the specified event.
    /// </summary>
    [HttpGet("{eventId}/categories")]
    public async Task<ActionResult<PagedResult<AwardCategoryDto>>> GetCategories(
        [FromRoute] string eventId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct);
        if (accessError is not null) return accessError;

        var categories = await _awardService.GetActiveCategoriesPagedAsync(eventId, skip, take, ct);
        return Ok(categories);
    }
}
