namespace Canhoes.Api.Models;

public record EventSummaryDto(
    string Id,
    string Name,
    bool IsActive
);

public record EventUserDto(
    Guid Id,
    string Name,
    string Role
);

public record EventPhaseDto(
    string Id,
    string Type,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsActive
);

public record EventContextDto(
    EventSummaryDto Event,
    List<EventUserDto> Users,
    List<EventPhaseDto> Phases,
    EventPhaseDto? ActivePhase
);

public record EventFeedPostDto(
    string Id,
    string EventId,
    Guid UserId,
    string UserName,
    string Content,
    string? ImageUrl,
    DateTimeOffset CreatedAt
);

public record CreateEventPostRequest(
    string Content,
    string? ImageUrl
);

public record EventCategoryDto(
    string Id,
    string EventId,
    string Title,
    string Kind,
    bool IsActive,
    string? Description
);

public record CreateEventCategoryRequest(
    string Title,
    string? Description,
    string Kind,
    int? SortOrder
);

public record EventVoteOptionDto(
    string Id,
    string CategoryId,
    string Label
);

public record EventVotingCategoryDto(
    string Id,
    string EventId,
    string Title,
    string Kind,
    string? Description,
    string? VoteQuestion,
    List<EventVoteOptionDto> Options,
    string? MyOptionId
);

public record EventVotingBoardDto(
    string EventId,
    string? PhaseId,
    bool CanVote,
    List<EventVotingCategoryDto> Categories
);

public record CreateEventVoteRequest(
    string CategoryId,
    string OptionId
);

public record EventVoteDto(
    string Id,
    Guid UserId,
    string CategoryId,
    string OptionId,
    string PhaseId
);

public record EventProposalDto(
    string Id,
    string EventId,
    Guid UserId,
    string Content,
    string Status,
    DateTimeOffset CreatedAt
);

public record CreateEventProposalRequest(string Content);

public record UpdateEventProposalRequest(string Status);

public record EventWishlistItemDto(
    string Id,
    Guid UserId,
    string EventId,
    string Title,
    string? Link,
    DateTimeOffset UpdatedAt
);

public record CreateEventWishlistItemRequest(
    string Title,
    string? Link
);
