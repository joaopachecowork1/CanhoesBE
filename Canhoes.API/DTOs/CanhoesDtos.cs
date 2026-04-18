namespace Canhoes.Api.DTOs;

// ------------------------------
// Canhões do Ano (fun awards)
// ------------------------------

public record AwardCategoryDto(
    string Id,
    string Name,
    int SortOrder,
    bool IsActive,
    string Kind,
    string? Description,
    string? VoteQuestion,
    string? VoteRules
);

/// <summary>
/// Lightweight category summary for list views — omits VoteQuestion, VoteRules
/// and Description to reduce payload size in dropdowns and navigation.
/// </summary>
public record AwardCategorySummaryDto(
    string Id,
    string Name,
    int SortOrder,
    bool IsActive,
    string Kind
);

public record CreateAwardCategoryRequest(
    string Name,
    int? SortOrder,
    string Kind,
    string? Description,
    string? VoteQuestion,
    string? VoteRules
);

public record UpdateAwardCategoryRequest(
    string? Name,
    int? SortOrder,
    bool? IsActive,
    string? Kind,
    string? Description,
    string? VoteQuestion,
    string? VoteRules
);

public record PublicUserDto(Guid Id, string Email, string? DisplayName, bool IsAdmin);

public record MeDto(PublicUserDto User);

public record UserVoteDto(string Id, string CategoryId, Guid VoterUserId, Guid TargetUserId, DateTimeOffset UpdatedAtUtc);

public record CastUserVoteRequest(string CategoryId, Guid TargetUserId);

public record NomineeDto(
    string Id,
    string? CategoryId,
    string Title,
    string? ImageUrl,
    string Status,
    DateTimeOffset CreatedAtUtc
);

/// <summary>
/// Lightweight nominee summary for list views — omits ImageUrl and timestamp
/// to reduce payload size in tables and queues.
/// </summary>
public record NomineeSummaryDto(
    string Id,
    string? CategoryId,
    string Title,
    string Status
);

public record CreateNomineeRequest(
    string? CategoryId,
    string Title,
    string? TargetUserId,
    string? Kind
);

public record CategoryProposalDto(string Id, string Name, string? Description, string Status, DateTimeOffset CreatedAtUtc);

public record CreateCategoryProposalRequest(string Name, string? Description);

public record GalaMeasureDto(string Id, string Text, bool IsActive, DateTimeOffset CreatedAtUtc);

public record MeasureProposalDto(string Id, string Text, string Status, DateTimeOffset CreatedAtUtc);

public record CreateMeasureProposalRequest(string Text);

public record UpdateMeasureProposalRequest(string? Text, string? Status);

public record SetNomineeCategoryRequest(string? CategoryId);

public record AdminVoteAuditRowDto(
    string CategoryId,
    string CategoryName,
    string NomineeId,
    Guid UserId,
    string UserName,
    DateTimeOffset UpdatedAtUtc
);

public record CanhoesResultNomineeDto(
    string NomineeId,
    string? CategoryId,
    string Title,
    string? ImageUrl,
    int Votes
);

public record CanhoesCategoryResultDto(
    string CategoryId,
    string CategoryName,
    int TotalVotes,
    List<CanhoesResultNomineeDto> Top
);

public record VoteDto(string Id, string CategoryId, string NomineeId, Guid UserId, DateTimeOffset UpdatedAtUtc);

public record CastVoteRequest(string CategoryId, string NomineeId);

public record CanhoesEventStateDto(string Phase, bool NominationsVisible, bool ResultsVisible);

/// <summary>
/// Generic paginated result for list endpoints.
/// </summary>
public record PagedResult<T>(
    List<T> Items,
    int Total,
    int Skip,
    int Take,
    bool HasMore
)
{
    public static PagedResult<T> Empty(int skip = 0, int take = 20) =>
        new([], 0, skip, take, false);
}

/// <summary>
/// Pagination parameters for list endpoints.
/// </summary>
public record PageRequest(
    int Skip = 0,
    int Take = 50
);

/// <summary>
/// Canonical paginated vote-audit payload for the admin surface.
/// </summary>
public record AdminVotesPagedDto(
    int Total,
    List<AdminVoteAuditRowDto> Votes,
    int Skip,
    int Take,
    bool HasMore
);

/// <summary>
/// Paginated nominations payload for the admin panel.
/// </summary>
public record AdminNomineesPagedDto(
    int Total,
    List<AdminNomineeDto> Nominations,
    int Skip,
    int Take,
    bool HasMore
);

/// <summary>
/// Counts-only payload for the admin bootstrap. Replaces full lists in the
/// bootstrap so the frontend can lazy-load each section on demand.
/// </summary>
public record AdminListCountsDto(
    int NomineesTotal,
    int AdminNomineesTotal,
    int VotesTotal,
    int CategoryProposalsTotal,
    int CategoryProposalsPendingTotal,
    int MeasureProposalsTotal,
    int MeasureProposalsPendingTotal,
    int MembersTotal,
    int OfficialResultsCategoriesCount
);

public record WishlistItemDto(
    string Id,
    Guid UserId,
    string Title,
    string? Url,
    string? Notes,
    string? ImageUrl,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

public record CreateWishlistItemRequest(string Title, string? Url, string? Notes);

public record SecretSantaMeDto(
    string DrawId,
    string EventCode,
    PublicUserDto Receiver
);

public record CreateSecretSantaDrawRequest(string? EventCode);

public record SecretSantaDrawDto(string Id, string EventCode, DateTimeOffset CreatedAtUtc, bool IsLocked);

// NOTE: Hub DTOs live in Canhoes.Api.DTOs (HubPostDto, HubCommentDto, polls, requests)
// to keep the API layer clean and avoid name collisions with domain models.
