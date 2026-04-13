using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Canhoes.Api.Auth;

/// <summary>
/// DEV ONLY.
/// Sets a user identity for request scoping when mock auth is enabled.
///
/// How it works:
/// - Reads MockUserEmail and AdminEmails from configuration.
/// - Creates claims with the mock email and marks the user as admin if the
///   email appears in the AdminEmails allowlist.
/// - The UserContextMiddleware will resolve this email to a real UserEntity.
/// </summary>
public sealed class MockAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _cfg;

    public MockAuthMiddleware(RequestDelegate next, IConfiguration cfg)
    {
        _next = next;
        _cfg = cfg;
    }

    public async Task Invoke(HttpContext ctx)
    {
        var mockEmail = _cfg["Auth:MockUserEmail"] ?? "dev@canhoes.com";
        var adminEmails = _cfg.GetSection("Auth:AdminEmails").Get<string[]>() ?? Array.Empty<string>();
        var isAdmin = adminEmails.Any(e => string.Equals(e.Trim(), mockEmail, StringComparison.OrdinalIgnoreCase));

        var claims = new List<Claim>
        {
            new Claim("sub", mockEmail),
            new Claim("email", mockEmail),
            new Claim(ClaimTypes.Email, mockEmail),
            new Claim("displayName", isAdmin ? "Mock Admin" : "Mock User"),
        };

        var identity = new ClaimsIdentity(claims, "MockAuthScheme");
        ctx.User = new ClaimsPrincipal(identity);

        // Store email and admin flag so UserContextMiddleware can resolve
        // or create the corresponding UserEntity on first access.
        ctx.Items["MockAuthEmail"] = mockEmail;
        ctx.Items["MockAuthIsAdmin"] = isAdmin;

        await _next(ctx);
    }
}
