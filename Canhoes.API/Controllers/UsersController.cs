using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Canhoes.Api.Auth;
using Canhoes.Api.Data;
using Canhoes.Api.Models;

namespace Canhoes.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly CanhoesDbContext _db;

    public UsersController(CanhoesDbContext db)
    {
        _db = db;
    }

    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> Me(CancellationToken ct)
    {
        var user = await ResolveCurrentUserAsync(HttpContext.User, ct);
        return user is null ? Unauthorized() : Ok(new MeDto(user));
    }

    [HttpGet("users")]
    public async Task<ActionResult<PagedResult<PublicUserDto>>> ListUsers(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var total = await _db.Users.CountAsync(ct);
        var list = await _db.Users.AsNoTracking()
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var items = list.Select(u => new PublicUserDto(u.Id, u.Email, u.DisplayName, u.IsAdmin)).ToList();
        return new PagedResult<PublicUserDto>(items, total, skip, take, (skip + take) < total);
    }

    // Simple admin management (optional; useful for a small private event)
    [HttpPost("users/{id:guid}/set-admin")]
    public async Task<ActionResult<PublicUserDto>> SetAdmin([FromRoute] Guid id, [FromQuery] bool isAdmin, CancellationToken ct)
    {
        if (!HttpContext.IsAdmin()) return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        user.IsAdmin = isAdmin;
        await _db.SaveChangesAsync(ct);

        return Ok(new PublicUserDto(user.Id, user.Email, user.DisplayName, user.IsAdmin));
    }

    private async Task<PublicUserDto?> ResolveCurrentUserAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var externalId = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue("email")
            ?? principal.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.ExternalId == externalId || u.Email == email)
            .Select(u => new PublicUserDto(u.Id, u.Email, u.DisplayName, u.IsAdmin))
            .FirstOrDefaultAsync(ct);

        if (user is not null)
        {
            return user;
        }

        var entity = new UserEntity
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Email = email,
            DisplayName = principal.FindFirstValue("name")
                ?? principal.FindFirstValue(ClaimTypes.Name)
                ?? principal.FindFirstValue("displayName")
                ?? email,
            IsAdmin = !await _db.Users.AnyAsync(ct),
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new PublicUserDto(entity.Id, entity.Email, entity.DisplayName, entity.IsAdmin);
    }
}
