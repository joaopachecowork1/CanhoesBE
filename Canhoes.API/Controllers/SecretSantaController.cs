using Canhoes.Api.Data;
using Canhoes.Api.Access;
using Canhoes.Api.DTOs;
using Canhoes.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;

namespace Canhoes.Api.Controllers;

public sealed class SecretSantaController : EventControllerBase {
    private readonly ISecretSantaService _secretSantaService;

    public SecretSantaController(
        ISecretSantaService secretSantaService,
        CanhoesDbContext db, 
        IMemoryCache cache, 
        IHubContext<Canhoes.Api.Hubs.EventHub> hub) 
        : base(db, cache, hub) 
    {
        _secretSantaService = secretSantaService;
    }

    /// <summary>
    /// Gets the Secret Santa overview, including assigned user and wishlist counts.
    /// </summary>
    [HttpGet("{eventId}/secret-santa/overview")]
    public async Task<ActionResult<EventSecretSantaOverviewDto>> GetSecretSantaOverview([FromRoute] string eventId, CancellationToken ct)
    {
        var (userAccess, _, accessError) = await RequireEventModuleAccessAsync(
            eventId,
            EventModuleKey.SecretSanta,
            ct);
        if (accessError is not null) return accessError;

        var overview = await _secretSantaService.GetOverviewAsync(eventId, userAccess.UserId, ct);
        return Ok(overview);
    }
}
