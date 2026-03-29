using System.Security.Claims;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Auth;

/// <summary>
/// Maps the authenticated Google user to the local user record used by the
/// application. The database is the source of truth for `IsAdmin`.
/// </summary>
public sealed class UserContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserContextMiddleware> _logger;
    private readonly IConfiguration _config;

    public UserContextMiddleware(
        RequestDelegate next,
        ILogger<UserContextMiddleware> logger,
        IConfiguration config)
    {
        _next = next;
        _logger = logger;
        _config = config;
    }

    public async Task Invoke(HttpContext context, CanhoesDbContext db)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var profile = ExtractProfile(context.User);
        if (profile is null)
        {
            await _next(context);
            return;
        }

        var ct = context.RequestAborted;
        var user = await db.Users.FirstOrDefaultAsync(
            x => x.ExternalId == profile.ExternalId || x.Email == profile.Email,
            ct);

        if (user is null)
        {
            user = await CreateUserAsync(profile, db, ct);
        }

        await PromoteAllowlistedAdminAsync(user, db, ct);

        context.Items["UserId"] = user.Id;
        context.Items["IsAdmin"] = user.IsAdmin;

        await _next(context);
    }

    private static AuthenticatedUserProfile? ExtractProfile(ClaimsPrincipal principal)
    {
        var externalId = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = principal.FindFirst("email")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value;
        var displayName = principal.FindFirst("name")?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value
            ?? email;

        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new AuthenticatedUserProfile(externalId, email, displayName ?? email);
    }

    private async Task<UserEntity> CreateUserAsync(
        AuthenticatedUserProfile profile,
        CanhoesDbContext db,
        CancellationToken ct)
    {
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = profile.Email,
            DisplayName = profile.DisplayName,
            ExternalId = profile.ExternalId,
            IsAdmin = !await db.Users.AnyAsync(ct),
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created local user {Email} (admin: {IsAdmin})",
            user.Email,
            user.IsAdmin);

        return user;
    }

    private async Task PromoteAllowlistedAdminAsync(
        UserEntity user,
        CanhoesDbContext db,
        CancellationToken ct)
    {
        var adminEmails = _config.GetSection("Auth:AdminEmails").Get<string[]>() ?? Array.Empty<string>();
        if (adminEmails.Length == 0 || user.IsAdmin)
        {
            return;
        }

        var isAllowlisted = adminEmails.Any(email =>
            string.Equals(email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase));

        if (!isAllowlisted)
        {
            return;
        }

        user.IsAdmin = true;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Promoted {Email} to admin via configured allowlist.", user.Email);
    }

    private sealed record AuthenticatedUserProfile(string ExternalId, string Email, string DisplayName);
}

public static class HttpContextUserExtensions
{
    public static Guid GetUserId(this HttpContext ctx)
    {
        if (ctx.Items.TryGetValue("UserId", out var value) && value is Guid userId)
        {
            return userId;
        }

        return Guid.Empty;
    }

    public static bool IsAdmin(this HttpContext ctx)
    {
        if (ctx.Items.TryGetValue("IsAdmin", out var value) && value is bool isAdmin)
        {
            return isAdmin;
        }

        return false;
    }
}
