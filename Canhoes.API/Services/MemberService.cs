using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using Canhoes.Api.Repositories;
using Canhoes.Api.Media;
using static Canhoes.Api.Mappers.EventMappers;

namespace Canhoes.Api.Services;

public sealed class MemberService : IMemberService
{
    private readonly IUserRepository _userRepository;
    private readonly IAwardRepository _awardRepository;
    private readonly IEventRepository _eventRepository;

    public MemberService(
        IUserRepository userRepository,
        IAwardRepository awardRepository,
        IEventRepository eventRepository)
    {
        _userRepository = userRepository;
        _awardRepository = awardRepository;
        _eventRepository = eventRepository;
    }

    public async Task<List<PublicUserDto>> GetMembersAsync(string eventId, CancellationToken ct)
    {
        var members = await _eventRepository.GetEventMembersAsync(eventId, ct);
        var userIds = members.Select(m => m.UserId).ToList();
        var users = await _userRepository.GetUsersAsync(userIds, ct);
        var usersById = users.ToDictionary(u => u.Id);

        return members
            .Where(m => usersById.ContainsKey(m.UserId))
            .OrderByDescending(m => m.Role == EventRoles.Admin)
            .ThenBy(m => usersById[m.UserId].DisplayName ?? usersById[m.UserId].Email)
            .Select(m => ToPublicUserDto(usersById[m.UserId], m.Role == EventRoles.Admin))
            .ToList();
    }

    public async Task<List<GalaMeasureDto>> GetMeasuresAsync(string eventId, CancellationToken ct)
    {
        var measures = await _awardRepository.GetMeasuresAsync(eventId, ct);
        return measures.Select(m => new GalaMeasureDto(
            m.Id,
            m.Text,
            m.IsActive,
            new DateTimeOffset(m.CreatedAtUtc, TimeSpan.Zero)
        )).ToList();
    }

    public async Task<MeasureProposalDto> CreateMeasureProposalAsync(string eventId, Guid userId, CreateMeasureProposalRequest request, CancellationToken ct)
    {
        var proposal = new MeasureProposalEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            ProposedByUserId = userId,
            Text = request.Text.Trim(),
            Status = ProposalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _awardRepository.AddMeasureProposalAsync(proposal, ct);
        await _awardRepository.SaveChangesAsync(ct);

        return ToMeasureProposalDto(proposal);
    }

    public async Task<List<CanhoesCategoryResultDto>> GetResultsAsync(string eventId, CancellationToken ct)
    {
        var categories = await _awardRepository.GetActiveCategoriesAsync(eventId, ct);
        if (categories.Count == 0) return new List<CanhoesCategoryResultDto>();

        var results = new List<CanhoesCategoryResultDto>();
        var nominees = await _awardRepository.GetApprovedNomineesAsync(eventId, ct);
        var nomineesByCategoryId = nominees
            .Where(n => n.CategoryId != null)
            .GroupBy(n => n.CategoryId!)
            .ToDictionary(g => g.Key, g => g.ToList());
        
        var nomineeVotes = await _awardRepository.GetNomineeVotesAsync(eventId, ct);
        var userVotes = await _awardRepository.GetUserVotesAsync(eventId, ct);
        
        var members = await _eventRepository.GetEventMembersAsync(eventId, ct);
        var users = await _userRepository.GetUsersAsync(members.Select(m => m.UserId).ToList(), ct);
        var usersById = users.ToDictionary(u => u.Id);

        foreach (var category in categories.OrderBy(c => c.SortOrder))
        {
            if (category.Kind == AwardCategoryKind.UserVote)
            {
                var topUsers = userVotes
                    .Where(v => v.CategoryId == category.Id)
                    .GroupBy(v => v.TargetUserId)
                    .Select(g => new CanhoesResultNomineeDto(
                        g.Key.ToString(),
                        category.Id,
                        usersById.TryGetValue(g.Key, out var u) ? (u.DisplayName ?? u.Email) : g.Key.ToString(),
                        null,
                        g.Count()
                    ))
                    .OrderByDescending(x => x.Votes)
                    .ThenBy(x => x.Title)
                    .Take(3)
                    .ToList();

                results.Add(new CanhoesCategoryResultDto(
                    category.Id,
                    category.Name,
                    userVotes.Count(v => v.CategoryId == category.Id),
                    topUsers
                ));
            }
            else
            {
                var catNominees = nomineesByCategoryId.TryGetValue(category.Id, out var n) ? n : new List<NomineeEntity>();
                var topNominees = catNominees
                    .Select(nom => new CanhoesResultNomineeDto(
                        nom.Id,
                        category.Id,
                        nom.Title,
                        MediaUrlFormatter.Normalize(nom.ImageUrl),
                        nomineeVotes.Count(v => v.NomineeId == nom.Id)
                    ))
                    .OrderByDescending(x => x.Votes)
                    .ThenBy(x => x.Title)
                    .Take(3)
                    .ToList();

                results.Add(new CanhoesCategoryResultDto(
                    category.Id,
                    category.Name,
                    nomineeVotes.Count(v => v.CategoryId == category.Id),
                    topNominees
                ));
            }
        }

        return results;
    }

    public async Task<MyNominationStatusDto> GetMyNominationStatusAsync(string eventId, Guid userId, CancellationToken ct)
    {
        var latest = await _awardRepository.GetLatestNomineeAsync(eventId, userId, ct);
        if (latest is null) return new MyNominationStatusDto(false, null, null, null, null);

        return new MyNominationStatusDto(
            true,
            latest.CategoryId,
            latest.Status,
            latest.Id,
            latest.Title
        );
    }

    public async Task<List<NomineeDto>> GetApprovedNomineesAsync(string eventId, CancellationToken ct)
    {
        var nominees = await _awardRepository.GetApprovedNomineesAsync(eventId, ct);
        return nominees.Select(n => ToNomineeDto(n) with { ImageUrl = MediaUrlFormatter.Normalize(n.ImageUrl) }).ToList();
    }

    public async Task<NomineeDto> CreateNominationAsync(string eventId, Guid userId, CreateNomineeRequest request, CancellationToken ct)
    {
        var nominee = new NomineeEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            CategoryId = string.IsNullOrWhiteSpace(request.CategoryId) ? null : request.CategoryId.Trim(),
            Title = request.Title.Trim(),
            SubmissionKind = request.Kind == "stickers" ? "stickers" : "nominees",
            SubmittedByUserId = userId,
            Status = ProposalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _awardRepository.AddNomineeAsync(nominee, ct);
        await _awardRepository.SaveChangesAsync(ct);

        return ToNomineeDto(nominee);
    }

    public async Task<NomineeDto?> UpdateNomineeImageAsync(string eventId, string nomineeId, Guid userId, bool isAdmin, string imageUrl, CancellationToken ct)
    {
        var nominee = await _awardRepository.GetNomineeAsync(nomineeId, eventId, ct);
        if (nominee is null) return null;
        if (nominee.SubmittedByUserId != userId && !isAdmin) return null;

        nominee.ImageUrl = imageUrl;
        await _awardRepository.SaveChangesAsync(ct);

        return ToNomineeDto(nominee) with { ImageUrl = MediaUrlFormatter.Normalize(nominee.ImageUrl) };
    }

    public async Task<EventWishlistItemDto?> UpdateWishlistImageAsync(string eventId, string itemId, Guid userId, bool isAdmin, string imageUrl, CancellationToken ct)
    {
        var item = await _eventRepository.GetWishlistItemAsync(itemId, eventId, ct);
        if (item is null) return null;
        if (item.UserId != userId && !isAdmin) return null;

        item.ImageUrl = imageUrl;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _eventRepository.SaveChangesAsync(ct);

        return ToEventWishlistItemDto(item);
    }

    public async Task<bool> DeleteWishlistItemAsync(string eventId, string itemId, Guid userId, bool isAdmin, CancellationToken ct)
    {
        var item = await _eventRepository.GetWishlistItemAsync(itemId, eventId, ct);
        if (item is null) return false;
        if (item.UserId != userId && !isAdmin) return false;

        await _eventRepository.DeleteWishlistItemAsync(item, ct);
        await _eventRepository.SaveChangesAsync(ct);
        return true;
    }
}
