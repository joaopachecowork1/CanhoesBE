using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Canhoes.Api.Auth;
using Canhoes.Api.DTOs;
using Canhoes.Api.Repositories;
using static Canhoes.Api.Mappers.EventMappers;

namespace Canhoes.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;

    public UsersController(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    /// <summary>
    /// Retrieves the currently authenticated user profile.
    /// If the user does not exist in the local database, a profile is automatically created.
    /// </summary>
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
        var externalId = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(email)) return null;

        var displayName = principal.FindFirstValue("name")
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("displayName");

        var user = await _userRepository.ResolveUserAsync(externalId, email, displayName, ct);
        return ToPublicUserDto(user, user.IsAdmin);
    }
}
