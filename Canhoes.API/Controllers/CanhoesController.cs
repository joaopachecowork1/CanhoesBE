using Canhoes.Api.Access;
using Canhoes.Api.Caching;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Canhoes.Api.Controllers;

[ApiController]
[Route("api/canhoes")]
[Authorize]
public partial class CanhoesController : ControllerBase
{
    private readonly CanhoesDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IMemoryCache _cache;

    private sealed record ActiveEventAccessContext(
        string EventId,
        Guid UserId,
        bool IsAdmin,
        bool IsMember,
        EventModuleAccessSnapshot ModuleAccess)
    {
        public bool CanAccess => IsAdmin || IsMember;
        public bool CanManage => IsAdmin;
    }

    public CanhoesController(CanhoesDbContext db, IWebHostEnvironment env, IMemoryCache cache)
    {
        _db = db;
        _env = env;
        _cache = cache;
    }
}
