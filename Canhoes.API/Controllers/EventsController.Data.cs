using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Controllers;

public sealed partial class EventsController
{
    private async Task ApplyCategoryProposalStatusAsync(
        CategoryProposalEntity proposal,
        string eventId,
        string status,
        CancellationToken ct)
    {
        proposal.Status = status;
        if (status != ProposalStatus.Approved) return;

        var categoryExists = await _db.AwardCategories
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId && x.Name == proposal.Name, ct);
        if (categoryExists) return;

        var nextSortOrder = (await _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;

        _db.AwardCategories.Add(new AwardCategoryEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            Name = proposal.Name,
            Description = proposal.Description,
            SortOrder = nextSortOrder,
            Kind = AwardCategoryKind.Sticker,
            IsActive = true
        });
    }

    private Task<AwardCategoryEntity?> FindCategoryAsync(
        string eventId,
        string categoryId,
        CancellationToken ct) =>
        _db.AwardCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == categoryId && x.EventId == eventId, ct);

    private Task<CategoryProposalEntity?> FindCategoryProposalAsync(
        string eventId,
        string proposalId,
        CancellationToken ct) =>
        _db.CategoryProposals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == proposalId && x.EventId == eventId, ct);

    private Task<MeasureProposalEntity?> FindMeasureProposalAsync(
        string eventId,
        string proposalId,
        CancellationToken ct) =>
        _db.MeasureProposals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == proposalId && x.EventId == eventId, ct);

    private Task<NomineeEntity?> FindNomineeAsync(
        string eventId,
        string nomineeId,
        CancellationToken ct) =>
        _db.Nominees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == nomineeId && x.EventId == eventId, ct);

    private Task<List<EventPhaseEntity>> LoadEventPhasesAsync(
        string eventId,
        CancellationToken ct) =>
        _db.EventPhases
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.StartDateUtc)
            .ToListAsync(ct);

    /// <summary>
    /// Consolidated counts query using an optimized fallback approach for cross-platform compatibility.
    /// </summary>
    private async Task<EventCountsAggregate> LoadEventCountsAsync(
        string eventId,
        CancellationToken ct)
    {
        return await LoadEventCountsFallbackAsync(eventId, ct);
    }

    private async Task<EventCountsAggregate> LoadEventCountsFallbackAsync(
        string eventId,
        CancellationToken ct)
    {
        var memberCount = await _db.EventMembers
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var hubPostCount = await _db.HubPosts
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        var pendingCategoryProposalsCount = await _db.CategoryProposals
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId && x.Status == ProposalStatus.Pending, ct);

        var wishlistCount = await _db.WishlistItems
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId, ct);

        return new EventCountsAggregate(
            memberCount,
            hubPostCount,
            pendingCategoryProposalsCount,
            wishlistCount);
    }

    private record EventCountsAggregate(
        int MemberCount,
        int HubPostCount,
        int PendingCategoryProposalsCount,
        int WishlistCount);

    private Task<List<AwardCategoryEntity>> LoadActiveCategoriesAsync(
        string eventId,
        CancellationToken ct) =>
        _db.AwardCategories
            .AsNoTracking()
            .Where(x => x.EventId == eventId && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

    private async Task<int> CountSubmittedVotesAsync(
        Guid userId,
        IReadOnlyCollection<string> categoryIds,
        CancellationToken ct)
    {
        if (categoryIds.Count == 0) return 0;

        return await _db.Votes
                   .AsNoTracking()
                   .CountAsync(x => x.UserId == userId && categoryIds.Contains(x.CategoryId), ct)
               + await _db.UserVotes
                   .AsNoTracking()
                   .CountAsync(x => x.VoterUserId == userId && categoryIds.Contains(x.CategoryId), ct);
    }

    private Task<int> CountWishlistItemsAsync(
        string eventId,
        Guid userId,
        CancellationToken ct) =>
        _db.WishlistItems
            .AsNoTracking()
            .CountAsync(x => x.EventId == eventId && x.UserId == userId, ct);

    private async Task<EventMemberDirectory> LoadEventMemberDirectoryAsync(
        string eventId,
        CancellationToken ct)
    {
        var members = await _db.EventMembers
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .ToListAsync(ct);

        var eventUserIds = members.Select(x => x.UserId).Distinct().ToList();
        var eventUsersById = await _db.Users
            .AsNoTracking()
            .Where(x => eventUserIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        return new EventMemberDirectory(members, eventUsersById);
    }

    private Task<List<EventPhaseEntity>> LoadEventPhasesForUpdateAsync(
        string eventId,
        CancellationToken ct) =>
        _db.EventPhases
            .Where(x => x.EventId == eventId)
            .OrderBy(x => x.StartDateUtc)
            .ToListAsync(ct);

    private async Task<AwardCategoryEntity> CreateCategoryEntityAsync(
        string eventId,
        string name,
        string? description,
        int? sortOrder,
        AwardCategoryKind kind,
        CancellationToken ct)
    {
        var nextSortOrder = sortOrder
            ?? (await _db.AwardCategories
                .AsNoTracking()
                .Where(x => x.EventId == eventId)
                .MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;

        var category = new AwardCategoryEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Kind = kind,
            SortOrder = nextSortOrder,
            IsActive = true
        };

        _db.AwardCategories.Add(category);
        await _db.SaveChangesAsync(ct);
        return category;
    }

    private async Task<List<UserEntity>> LoadEventSecretSantaParticipantsAsync(
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
