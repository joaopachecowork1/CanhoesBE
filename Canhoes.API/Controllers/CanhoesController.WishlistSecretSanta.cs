using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public partial class CanhoesController
{
    [HttpGet("members")]
    public async Task<ActionResult<PagedResult<PublicUserDto>>> GetMembers(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Wishlist);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var memberIds = await _db.EventMembers.AsNoTracking()
            .Where(x => x.EventId == access.EventId)
            .Select(x => x.UserId)
            .ToListAsync(ct);

        var total = memberIds.Count;
        var list = await _db.Users.AsNoTracking()
            .Where(u => memberIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var items = list.Select(ToPublicUserDto).ToList();
        return new PagedResult<PublicUserDto>(items, total, skip, take, (skip + take) < total);
    }

    [HttpGet("wishlist")]
    public async Task<ActionResult<PagedResult<WishlistItemDto>>> GetWishlist(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Wishlist);
        if (error is not null) return error;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var total = await _db.WishlistItems.AsNoTracking()
            .CountAsync(x => x.EventId == access.EventId, ct);

        var items = await _db.WishlistItems.AsNoTracking()
            .Where(x => x.EventId == access.EventId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var dtos = items.Select(ToWishlistItemDto).ToList();
        return new PagedResult<WishlistItemDto>(dtos, total, skip, take, (skip + take) < total);
    }

    [HttpPost("wishlist")]
    public async Task<ActionResult<WishlistItemDto>> CreateWishlistItem([FromBody] CreateWishlistItemRequest req, CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Wishlist);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title is required.");

        var item = new WishlistItemEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = access.EventId,
            UserId = access.UserId,
            Title = req.Title.Trim(),
            Url = string.IsNullOrWhiteSpace(req.Url) ? null : req.Url.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.WishlistItems.Add(item);
        await _db.SaveChangesAsync(ct);

        return ToWishlistItemDto(item);
    }

    [HttpPost("wishlist/{id}/upload")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadWishlistImage([FromRoute] string id, IFormFile file, CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Wishlist);
        if (error is not null) return error;

        var item = await FindWishlistItemAsync(access.EventId, id, ct);
        if (item is null) return NotFound();
        if (item.UserId != access.UserId && !access.IsAdmin) return Forbid();

        if (file is null || file.Length == 0) return BadRequest("File is required.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg")) return BadRequest("Only PNG/JPG allowed.");

        var folder = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", "canhoes", "wishlist");
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var full = Path.Combine(folder, fileName);
        await using (var fs = System.IO.File.Create(full))
        {
            await file.CopyToAsync(fs, ct);
        }

        item.ImageUrl = $"/uploads/canhoes/wishlist/{fileName}";
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpDelete("wishlist/{id}")]
    public async Task<IActionResult> DeleteWishlistItem([FromRoute] string id, CancellationToken ct)
    {
        var (access, error) = await RequireActiveEventAccessAsync(ct, moduleKey: EventModuleKey.Wishlist);
        if (error is not null) return error;

        var item = await FindWishlistItemAsync(access.EventId, id, ct);
        if (item is null) return NotFound();
        if (item.UserId != access.UserId && !access.IsAdmin) return Forbid();

        _db.WishlistItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("secret-santa/draw")]
    public async Task<ActionResult<SecretSantaDrawDto>> AdminDrawSecretSanta(
        [FromBody] CreateSecretSantaDrawRequest req,
        CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();

        var (activeEventId, error) = await RequireAdminActiveEventIdAsync(ct);
        if (error is not null) return error;

        var eventCode = string.IsNullOrWhiteSpace(req.EventCode)
            ? $"canhoes{DateTime.UtcNow.Year}"
            : req.EventCode.Trim();

        var existingDraws = await _db.SecretSantaDraws
            .Where(x => x.EventCode == eventCode)
            .ToListAsync(ct);

        if (existingDraws.Any())
        {
            foreach (var oldDraw in existingDraws)
            {
                var oldAssignments = _db.SecretSantaAssignments.Where(a => a.DrawId == oldDraw.Id);
                _db.SecretSantaAssignments.RemoveRange(oldAssignments);
            }

            _db.SecretSantaDraws.RemoveRange(existingDraws);
            await _db.SaveChangesAsync(ct);
        }

        // FIX: Load only event members instead of ALL users in the system
        var memberIds = await _db.EventMembers
            .AsNoTracking()
            .Where(x => x.EventId == activeEventId)
            .Select(x => x.UserId)
            .ToListAsync(ct);

        var users = await _db.Users.AsNoTracking()
            .Where(u => memberIds.Contains(u.Id))
            .OrderBy(u => u.Id)
            .ToListAsync(ct);

        if (users.Count < 2) return BadRequest("Need at least 2 members to draw.");

        var rng = Random.Shared;
        int maxAttempts = 100;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var shuffled = users.ToList();
            for (var i = shuffled.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            var isValid = true;
            for (var i = 0; i < users.Count; i++)
            {
                if (users[i].Id != shuffled[i].Id) continue;
                isValid = false;
                break;
            }

            if (!isValid) continue;

            var draw = new SecretSantaDrawEntity
            {
                Id = Guid.NewGuid().ToString(),
                EventCode = eventCode,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = HttpContext.GetUserId(),
                IsLocked = true
            };

            _db.SecretSantaDraws.Add(draw);

            for (var i = 0; i < users.Count; i++)
            {
                _db.SecretSantaAssignments.Add(new SecretSantaAssignmentEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    DrawId = draw.Id,
                    GiverUserId = users[i].Id,
                    ReceiverUserId = shuffled[i].Id
                });
            }

            await _db.SaveChangesAsync(ct);
            return ToSecretSantaDrawDto(draw);
        }

        return BadRequest($"Could not create a valid draw after {maxAttempts} attempts. Please try again.");
    }

    [HttpGet("secret-santa/me")]
    public async Task<ActionResult<SecretSantaMeDto>> GetMySecretSanta(
        [FromQuery] string? eventCode,
        CancellationToken ct)
    {
        var code = string.IsNullOrWhiteSpace(eventCode)
            ? $"canhoes{DateTime.UtcNow.Year}"
            : eventCode.Trim();

        var draw = await _db.SecretSantaDraws
            .AsNoTracking()
            .Where(d => d.EventCode == code)
            .OrderByDescending(d => d.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (draw is null) return NotFound(new { message = "No draw exists for this event yet." });

        var userId = HttpContext.GetUserId();
        var assignment = await _db.SecretSantaAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DrawId == draw.Id && x.GiverUserId == userId, ct);
        if (assignment is null) return NotFound(new { message = "You are not part of this draw." });

        var receiver = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == assignment.ReceiverUserId, ct);
        if (receiver is null) return NotFound(new { message = "Receiver user not found." });

        return new SecretSantaMeDto(draw.Id, draw.EventCode, ToPublicUserDto(receiver));
    }
}
