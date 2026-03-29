using Canhoes.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("uploads")]
public sealed class UploadsController : ControllerBase
{
    private readonly CanhoesDbContext _db;
    private readonly IWebHostEnvironment _env;

    public UploadsController(CanhoesDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    /// <summary>
    /// Serves uploaded media when the physical file no longer exists in App Service.
    /// Static files still handle the happy path first; this controller only acts as
    /// a fallback for missing upload paths, primarily Hub media persisted in SQL.
    /// </summary>
    [HttpGet("{**path}")]
    public async Task<IActionResult> GetUpload([FromRoute] string? path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return NotFound();
        }

        // Route("uploads") strips the leading segment before the catch-all binds.
        // For /uploads/hub/file.png the action receives "hub/file.png", so we
        // must rebuild the canonical uploads path before checking disk or SQL.
        var routeRelativePath = path.Trim().Replace('\\', '/').TrimStart('/');
        var uploadsRelativePath = Path.Combine("uploads", routeRelativePath)
            .Replace('\\', '/');
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var physicalPath = Path.GetFullPath(Path.Combine(webRoot, uploadsRelativePath));
        var fullWebRoot = Path.GetFullPath(webRoot);

        // Prevent traversal while still allowing the current static file layout.
        if (!physicalPath.StartsWith(fullWebRoot, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        if (System.IO.File.Exists(physicalPath))
        {
            return PhysicalFile(physicalPath, GetContentType(physicalPath));
        }

        var normalizedUrl = "/" + uploadsRelativePath;
        if (!normalizedUrl.StartsWith("/uploads/hub/", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var fileName = Path.GetFileName(routeRelativePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return NotFound();
        }

        var media = await _db.HubPostMedia
            .AsNoTracking()
            .Where(x => x.ContentBytes.Length > 0)
            .Where(x => x.Url == normalizedUrl || x.Url.EndsWith("/" + fileName))
            .OrderByDescending(x => x.UploadedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (media is null)
        {
            return NotFound();
        }

        var contentType = string.IsNullOrWhiteSpace(media.ContentType)
            ? GetContentType(fileName)
            : media.ContentType;

        return File(media.ContentBytes, contentType);
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
