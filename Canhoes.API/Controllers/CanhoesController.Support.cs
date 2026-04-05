using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Models;
using Canhoes.Api.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public partial class CanhoesController
{
    private bool IsAdmin() => HttpContext.IsAdmin();

    private async Task<(ActiveEventAccessContext Access, ActionResult? Error)> RequireActiveEventAccessAsync(
        CancellationToken ct,
        bool requireManage = false,
        EventModuleKey? moduleKey = null)
    {
        var activeEventId = await ResolveActiveEventIdAsync(ct);
        if (activeEventId is null) return (default!, NotFound());

        var userId = HttpContext.GetUserId();
        var isAdmin = HttpContext.IsAdmin();
        var isMember = await _db.EventMembers
            .AsNoTracking()
            .AnyAsync(x => x.EventId == activeEventId && x.UserId == userId, ct);

        var access = new ActiveEventAccessContext(
            activeEventId,
            userId,
            isAdmin,
            isMember,
            await EventModuleAccessEvaluator.EvaluateAsync(_db, activeEventId, userId, isAdmin, ct));

        if (requireManage ? !access.CanManage : !access.CanAccess) return (default!, Forbid());
        if (moduleKey.HasValue && !EventModuleAccessEvaluator.IsModuleEnabled(access.ModuleAccess.EffectiveModules, moduleKey.Value))
            return (default!, Forbid());

        return (access, null);
    }

    private Task<string?> ResolveActiveEventIdAsync(CancellationToken ct) =>
        _db.Events
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);

    private async Task<(string EventId, ActionResult? Error)> RequireActiveEventIdAsync(CancellationToken ct) =>
        (await ResolveActiveEventIdAsync(ct)) is { } activeEventId
            ? (activeEventId, null)
            : (string.Empty, NotFound());

    private async Task<(string EventId, ActionResult? Error)> RequireAdminActiveEventIdAsync(CancellationToken ct)
    {
        if (!IsAdmin()) return (string.Empty, Forbid());
        return await RequireActiveEventIdAsync(ct);
    }

    private Task<List<string>> LoadEventCategoryIdsAsync(string eventId, CancellationToken ct) =>
        _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.Id)
            .ToListAsync(ct);

    private Task<CategoryProposalEntity?> FindCategoryProposalAsync(
        string eventId,
        string proposalId,
        CancellationToken ct) =>
        _db.CategoryProposals.FirstOrDefaultAsync(
            x => x.Id == proposalId && x.EventId == eventId,
            ct);

    private Task<MeasureProposalEntity?> FindMeasureProposalAsync(
        string eventId,
        string proposalId,
        CancellationToken ct) =>
        _db.MeasureProposals.FirstOrDefaultAsync(
            x => x.Id == proposalId && x.EventId == eventId,
            ct);

    private Task<NomineeEntity?> FindNomineeAsync(
        string eventId,
        string nomineeId,
        CancellationToken ct) =>
        _db.Nominees.FirstOrDefaultAsync(
            x => x.Id == nomineeId && x.EventId == eventId,
            ct);

    private Task<WishlistItemEntity?> FindWishlistItemAsync(
        string eventId,
        string wishlistItemId,
        CancellationToken ct) =>
        _db.WishlistItems.FirstOrDefaultAsync(
            x => x.Id == wishlistItemId && x.EventId == eventId,
            ct);

    private static string NormalizeNomineeKind(string? kind) =>
        kind?.Trim().ToLowerInvariant() switch
        {
            "stickers" => "stickers",
            _ => "nominees"
        };

    private static EventModuleKey GetNomineeModuleKey(string? nomineeKind) =>
        string.Equals(NormalizeNomineeKind(nomineeKind), "stickers", StringComparison.Ordinal)
            ? EventModuleKey.Stickers
            : EventModuleKey.Nominees;
}
