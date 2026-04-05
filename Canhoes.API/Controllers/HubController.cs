using Canhoes.Api.Access;
using Canhoes.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Canhoes.Api.Controllers;

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
