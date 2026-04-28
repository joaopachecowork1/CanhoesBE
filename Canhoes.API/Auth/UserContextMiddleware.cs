using System.Linq;
using System;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Canhoes.Api.Auth;

/// <summary>
/// Maps the authenticated Google user (JWT id_token) to a local UserEntity in the database.
///
/// Rules (simple by design):
/// - If this is the first user ever created in the DB, they become admin.
/// - Admin status is stored in the DB (UserEntity.IsAdmin).
///
/// Controllers can use HttpContextUserExtensions.GetUserId()/IsAdmin().
/// </summary>
public sealed class UserContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserContextMiddleware> _logger;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;

    public UserContextMiddleware(RequestDelegate next, ILogger<UserContextMiddleware> logger, IConfiguration config, IMemoryCache cache)
    {
        _next = next;
        _logger = logger;
        _config = config;
        _cache = cache;
    }

    public async Task Invoke(HttpContext ctx, CanhoesDbContext _db)
    {

        var isMockAuth = ctx.Items.TryGetValue("MockAuthEmail", out var mockEmailObj)
            && mockEmailObj is string mockEmail;

        if (ctx.User?.Identity?.IsAuthenticated == true || isMockAuth)
        {
            string? sub = null;
            string? email = null;

            if (isMockAuth)
            {
                // Mock auth: use the email stored in Items by MockAuthMiddleware
                email = mockEmailObj as string;
                sub = email; // Use email as sub for mock users
            }
            else
            {
                // Real Google auth: extract claims
                var principal = ctx.User!;
                sub = principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                email = principal.FindFirst("email")?.Value
                        ?? principal.FindFirst(ClaimTypes.Email)?.Value;
            }

            var name = ctx.User?.FindFirst("name")?.Value
                       ?? ctx.User?.FindFirst(ClaimTypes.Name)?.Value
                       ?? ctx.User?.FindFirst("displayName")?.Value
                       ?? email;

            if (!string.IsNullOrWhiteSpace(sub) && !string.IsNullOrWhiteSpace(email))
            {
                var isAdminFromMock = isMockAuth
                    && ctx.Items.TryGetValue("MockAuthIsAdmin", out var adminObj)
                    && adminObj is bool adminBool && adminBool;
                var isAdminFromAllowlist = IsAdminEmail(email);
                var shouldForceAdmin = isAdminFromMock || isAdminFromAllowlist;
                var cacheKey = $"user-context:{sub}:{email}";
                if (_cache.TryGetValue(cacheKey, out UserContextSnapshot? cached) && cached is not null)
                {
                    if (!cached.IsAdmin && shouldForceAdmin)
                    {
                        cached = cached with
                        {
                            User = cached.User with { IsAdmin = true },
                            IsAdmin = true
                        };
                        _cache.Set(
                            cacheKey,
                            cached,
                            new MemoryCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                                SlidingExpiration = TimeSpan.FromMinutes(1)
                            });
                    }

                    ApplyContext(ctx, cached);
                    await _next(ctx);
                    return;
                }

                // Procura por ExternalId (mais correto) ou Email como fallback
                var user = await _db.Users.FirstOrDefaultAsync(u => u.ExternalId == sub || u.Email == email, ctx.RequestAborted);

                if (user == null)
                {
                    user = new UserEntity
                    {
                        Id = Guid.NewGuid(),
                        Email = email,
                        DisplayName = name,
                        ExternalId = sub,
                        IsAdmin = shouldForceAdmin || !await _db.Users.AnyAsync(ctx.RequestAborted),
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Users.Add(user);
                    await _db.SaveChangesAsync(ctx.RequestAborted);
                }

                if (shouldForceAdmin && !user.IsAdmin)
                {
                    user.IsAdmin = true;
                    await _db.SaveChangesAsync(ctx.RequestAborted);
                }

                var snapshot = new UserContextSnapshot(
                    new PublicUserDto(user.Id, user.Email, user.DisplayName, user.IsAdmin),
                    user.Id,
                    user.IsAdmin);

                _cache.Set(
                    cacheKey,
                    snapshot,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                        SlidingExpiration = TimeSpan.FromMinutes(1)
                    });

                ApplyContext(ctx, snapshot);
                _logger.LogDebug("Mapped token -> user: sub={Sub} email={Email} userId={UserId} isAdmin={IsAdmin}", sub, email, user.Id, user.IsAdmin);
            }
        }

        await _next(ctx);
    }

    private bool IsAdminEmail(string email)
    {
        try
        {
            var adminEmails = _config.GetSection("Auth:AdminEmails").Get<string[]>() ?? Array.Empty<string>();
            return adminEmails.Any(e => string.Equals(e.Trim(), email, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply AdminEmails allowlist.");
            return false;
        }
    }

    private static void ApplyContext(HttpContext ctx, UserContextSnapshot snapshot)
    {
        ctx.Items["CurrentUser"] = snapshot.User;
        ctx.Items["UserId"] = snapshot.UserId;
        ctx.Items["IsAdmin"] = snapshot.IsAdmin;

        if (snapshot.IsAdmin && ctx.User?.Identity is ClaimsIdentity identity)
        {
            if (!identity.HasClaim(c => (c.Type == "role" || c.Type == ClaimTypes.Role) && c.Value == "admin"))
            {
                identity.AddClaim(new Claim("role", "admin"));
            }
        }
    }

    private sealed record UserContextSnapshot(PublicUserDto User, Guid UserId, bool IsAdmin);
}

public static class HttpContextUserExtensions
{
    public static Guid GetUserId(this HttpContext ctx)
    {
        if (ctx.Items.TryGetValue("UserId", out var v) && v is Guid g) return g;
        return Guid.Empty;
    }

    public static bool IsAdmin(this HttpContext ctx)
    {
        if (ctx.Items.TryGetValue("IsAdmin", out var v) && v is bool b) return b;
        return false;
    }
}
