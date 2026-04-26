using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Canhoes.Api.Auth;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Canhoes.Api.DTOs;

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

    /// <summary>
    /// Retrieves the currently authenticated user profile.
    /// If the user does not exist in the local database, a profile is automatically created.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user profile information.</returns>
    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> Me(CancellationToken ct)
    {
        if (HttpContext.Items.TryGetValue("CurrentUser", out var currentUser) && currentUser is PublicUserDto cachedUser)
        {
            return Ok(new MeDto(cachedUser));
        }

        var resolvedUser = await ResolveCurrentUserAsync(HttpContext.User, ct);
        return resolvedUser is null ? Unauthorized() : Ok(new MeDto(resolvedUser));
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

        var existingUserDto = await _db.Users.AsNoTracking()
            .Where(u => u.ExternalId == externalId || u.Email == email)
            .Select(u => new PublicUserDto(u.Id, u.Email, u.DisplayName, u.IsAdmin))
            .FirstOrDefaultAsync(ct);

        if (existingUserDto is not null)
        {
            return existingUserDto;
        }

        var newUserEntity = new UserEntity
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

        _db.Users.Add(newUserEntity);
        await _db.SaveChangesAsync(ct);

        return new PublicUserDto(newUserEntity.Id, newUserEntity.Email, newUserEntity.DisplayName, newUserEntity.IsAdmin);
    }
}
