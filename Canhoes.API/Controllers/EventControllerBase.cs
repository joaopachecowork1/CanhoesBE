using Canhoes.Api.Access;
using Canhoes.Api.Auth;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Canhoes.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Hosting;
using static Canhoes.Api.Mappers.EventMappers;

namespace Canhoes.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
public abstract class EventControllerBase : ControllerBase
{
    protected readonly CanhoesDbContext _db;
    protected readonly IMemoryCache _cache;
    protected readonly Microsoft.AspNetCore.SignalR.IHubContext<Canhoes.Api.Hubs.EventHub> _hub;
    protected readonly IWebHostEnvironment? _env;

    public sealed record EventAccessContext(
        EventEntity Event,
        Guid UserId,
        bool IsAdmin,
        bool IsMember)
    {
        public bool CanAccess => IsAdmin || IsMember;
        public bool CanManage => IsAdmin;
    }

    protected EventControllerBase(
        CanhoesDbContext db, 
        IMemoryCache cache = null, 
        Microsoft.AspNetCore.SignalR.IHubContext<Canhoes.Api.Hubs.EventHub> hub = null,
        IWebHostEnvironment? env = null)
    {
        _db = db;
        _cache = cache;
        _hub = hub;
        _env = env;
    }

    protected async Task<ActionResult?> RequireManageAccessAsync(string eventId, CancellationToken ct) =>
        (await RequireEventAccessAsync(eventId, ct, requireManage: true)).accessError;

    protected async Task<(EventAccessContext eventAccess, ActionResult? accessError)> RequireEventAccessAsync(
        string eventId,
        CancellationToken ct,
        bool requireManage = false)
    {
        var eventAccess = await GetEventAccessAsync(eventId, ct);
        if (eventAccess is null) return (default!, NotFound());
        if (requireManage ? !eventAccess.CanManage : !eventAccess.CanAccess) return (default!, Forbid());
        return (eventAccess, null);
    }

    protected async Task<(EventAccessContext eventAccess, EventModuleAccessSnapshot moduleAccessSnapshot, ActionResult? accessError)> RequireEventModuleAccessAsync(
        string eventId,
        EventModuleKey moduleKey,
        CancellationToken ct)
    {
        var (eventAccess, accessError) = await RequireEventAccessAsync(eventId, ct);
        if (accessError is not null) return (default!, default!, accessError);

        var moduleAccessSnapshot = await EventModuleAccessEvaluator.EvaluateAsync(
            _db,
            eventId,
            eventAccess.UserId,
            eventAccess.IsAdmin,
            ct);
        if (!EventModuleAccessEvaluator.IsModuleEnabled(moduleAccessSnapshot.EffectiveModules, moduleKey))
        {
            return (default!, default!, Forbid());
        }

        return (eventAccess, moduleAccessSnapshot, null);
    }

    protected async Task<EventAccessContext?> GetEventAccessAsync(string eventId, CancellationToken ct)
    {
        var eventEntity = await _db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eventId, ct);
        if (eventEntity is null) return null;

        var currentUserId = HttpContext.GetUserId();
        var isCurrentUserAdmin = HttpContext.IsAdmin();
        var isCurrentUserMember = await _db.EventMembers
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId && x.UserId == currentUserId, ct);

        return new EventAccessContext(eventEntity, currentUserId, isCurrentUserAdmin, isCurrentUserMember);
    }

    protected static string NormalizeSecretSantaEventCode(
        string eventId,
        string? requestedEventCode) =>
        string.IsNullOrWhiteSpace(requestedEventCode) ? eventId : requestedEventCode.Trim();

    protected static string? NormalizeProposalStatusFilter(string? proposalStatus) =>
        string.IsNullOrWhiteSpace(proposalStatus) ? null : NormalizeProposalStatus(proposalStatus);

    protected static string? NormalizeProposalStatus(string proposalStatus)
    {
        var normalizedProposalStatus = proposalStatus.Trim().ToLowerInvariant();
        return ProposalStatus.IsValid(normalizedProposalStatus) ? normalizedProposalStatus : null;
    }

    protected static string? NormalizeNomineeStatusFilter(string? nomineeStatus)
    {
        if (string.IsNullOrWhiteSpace(nomineeStatus)) return null;

        var normalizedNomineeStatus = nomineeStatus.Trim().ToLowerInvariant();
        return ProposalStatus.IsValid(normalizedNomineeStatus) ? normalizedNomineeStatus : null;
    }

    protected record EventMemberDirectory(List<EventMemberEntity> Members, Dictionary<Guid, UserEntity> UsersById);

    protected async Task<EventMemberDirectory> LoadEventMemberDirectoryAsync(string eventId, CancellationToken ct)
    {
        var members = await _db.EventMembers
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .ToListAsync(ct);

        var userIds = members.Select(x => x.UserId).Distinct().ToList();
        var usersById = await _db.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        return new EventMemberDirectory(members, usersById);
    }

    protected record EventAggregateCounts(
        int MemberCount,
        int HubPostCount,
        int PendingCategoryProposalsCount,
        int WishlistCount);

    protected async Task<EventAggregateCounts> LoadEventCountsAsync(string eventId, CancellationToken ct)
    {
        var memberCount = await _db.EventMembers.CountAsync(x => x.EventId == eventId, ct);
        var hubPostCount = await _db.HubPosts.CountAsync(x => x.EventId == eventId, ct);
        var pendingCategoryProposalsCount = await _db.CategoryProposals.CountAsync(x => x.EventId == eventId && x.Status == ProposalStatus.Pending, ct);
        var wishlistCount = await _db.WishlistItems.CountAsync(x => x.EventId == eventId, ct);

        return new EventAggregateCounts(
            memberCount,
            hubPostCount,
            pendingCategoryProposalsCount,
            wishlistCount);
    }

    protected async Task<CanhoesEventStateEntity> GetOrCreateEventStateAsync(string eventId, CancellationToken ct)
    {
        var state = await _db.CanhoesEventState.FirstOrDefaultAsync(x => x.EventId == eventId, ct);
        if (state is null)
        {
            state = new CanhoesEventStateEntity
            {
                EventId = eventId,
                Phase = LegacyPhaseNames.Nominations,
                NominationsVisible = true,
                ResultsVisible = false,
                ModuleVisibilityJson = "{}"
            };
            _db.CanhoesEventState.Add(state);
            await _db.SaveChangesAsync(ct);
        }
        return state;
    }

    protected async Task<List<EventPhaseEntity>> LoadEventPhasesAsync(string eventId, CancellationToken ct)
    {
        return await _db.EventPhases
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.StartDateUtc)
            .ToListAsync(ct);
    }

    protected async Task<EventPhaseEntity?> GetActivePhaseAsync(string eventId, string phaseType, CancellationToken ct) =>
        await _db.EventPhases
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.Type == phaseType && x.IsActive)
            .OrderByDescending(x => x.StartDateUtc)
            .FirstOrDefaultAsync(ct);

    protected async Task<List<EventPhaseEntity>> LoadEventPhasesForUpdateAsync(string eventId, CancellationToken ct)
    {
        return await _db.EventPhases
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.StartDateUtc)
            .ToListAsync(ct);
    }

    protected async Task<SecretSantaDrawEntity?> GetLatestSecretSantaDrawAsync(string eventId, CancellationToken ct)
    {
        return await _db.SecretSantaDraws
            .AsNoTracking()
            .Where(x => x.EventCode == eventId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    protected async Task<MeasureProposalEntity?> FindMeasureProposalAsync(string eventId, string proposalId, CancellationToken ct) =>
        await _db.MeasureProposals.FirstOrDefaultAsync(x => x.EventId == eventId && x.Id == proposalId, ct);

    protected async Task<CategoryProposalEntity?> FindCategoryProposalAsync(string eventId, string proposalId, CancellationToken ct) =>
        await _db.CategoryProposals.FirstOrDefaultAsync(x => x.EventId == eventId && x.Id == proposalId, ct);

    protected async Task<NomineeEntity?> FindNomineeAsync(string eventId, string nomineeId, CancellationToken ct) =>
        await _db.Nominees.FirstOrDefaultAsync(x => x.EventId == eventId && x.Id == nomineeId, ct);

    protected async Task<AwardCategoryEntity?> FindAwardCategoryAsync(string eventId, string categoryId, CancellationToken ct) =>
        await _db.AwardCategories.FirstOrDefaultAsync(x => x.EventId == eventId && x.Id == categoryId, ct);

    protected async Task<List<AwardCategoryEntity>> LoadActiveCategoriesAsync(string eventId, CancellationToken ct) =>
        await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

    protected async Task<int> CountSubmittedVotesAsync(Guid userId, IReadOnlyCollection<string> categoryIds, CancellationToken ct)
    {
        var nv = await _db.Votes.CountAsync(x => categoryIds.Contains(x.CategoryId) && x.UserId == userId, ct);
        var uv = await _db.UserVotes.CountAsync(x => categoryIds.Contains(x.CategoryId) && x.VoterUserId == userId, ct);
        return nv + uv;
    }

    protected bool IsPhaseOpen(EventPhaseEntity? phase)
    {
        if (phase is null || !phase.IsActive) return false;
        var now = DateTime.UtcNow;
        return now >= phase.StartDateUtc && now <= phase.EndDateUtc;
    }

    protected async Task<List<UserEntity>> LoadEventSecretSantaParticipantsAsync(
        string eventId,
        CancellationToken ct)
    {
        var participantIds = await _db.EventMembers
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        return await _db.Users
            .AsNoTracking()
            .Where(x => participantIds.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
    }
}
