using Canhoes.Api.Access;
using Canhoes.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Canhoes.Api.Controllers;

/// <summary>
/// Legacy hub controller. All functionality has been migrated to the
/// event-scoped endpoints under <c>api/v1/events/{eventId}/feed/*</c>.
/// This controller is kept for backward compatibility and will be removed
/// once the frontend fully migrates away from it.
/// </summary>
[Obsolete("Use EventsController feed endpoints under api/v1/events/{eventId}/feed/* instead.")]
[ApiController]
[Route("api/hub")]
[Authorize]
public sealed partial class HubController : ControllerBase
{
    private readonly CanhoesDbContext _db;
    private readonly IWebHostEnvironment _env;

    private sealed record ActiveFeedAccessContext(
        string EventId,
        Guid UserId,
        bool IsAdmin,
        bool IsMember,
        EventModuleAccessSnapshot ModuleAccess)
    {
        public bool CanAccess => IsAdmin || IsMember;
        public bool CanManage => IsAdmin;
    }

    private static readonly HashSet<string> AllowedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    public HubController(CanhoesDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }
}
