namespace Canhoes.Api.DTOs;

/// <summary>
/// Minimal event identity used by chrome, lists and overview payloads.
/// </summary>
public record EventSummaryDto(
    string Id,
    string Name,
    bool IsActive
);

/// <summary>
/// Public event member projection consumed by the frontend.
/// </summary>
public record EventUserDto(
    Guid Id,
    string Name,
    string Role
);

/// <summary>
/// Single scheduled phase window for the current event cycle.
/// </summary>
public record EventPhaseDto(
    string Id,
    string Type,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    bool IsActive
);

/// <summary>
/// Full context payload for event-aware pages that need members plus phase
/// schedule in the same request.
/// </summary>
public record EventContextDto(
    EventSummaryDto Event,
    List<EventUserDto> Users,
    List<EventPhaseDto> Phases,
    EventPhaseDto? ActivePhase
);

/// <summary>
/// Member capabilities derived from backend auth and current event rules.
/// </summary>
public record EventPermissionsDto(
    bool IsAdmin,
    bool IsMember,
    bool CanPost,
    bool CanSubmitProposal,
    bool CanVote,
    bool CanManage
);

/// <summary>
/// High-level counters surfaced in the event home and shell.
/// </summary>
public record EventCountsDto(
    int MemberCount,
    int FeedPostCount,
    int CategoryCount,
    int PendingProposalCount,
    int WishlistItemCount
);

/// <summary>
/// Controls which event modules should be visible to regular members for the
/// current event state. The frontend uses this to keep navigation aligned with
/// the active phase instead of exposing every page all the time.
/// </summary>
public record EventModulesDto(
    bool Feed,
    bool SecretSanta,
    bool Wishlist,
    bool Categories,
    bool Voting,
    bool Gala,
    bool Stickers,
    bool Measures,
    bool Nominees,
    bool Admin
);

public record UpdateEventModulesRequest(
    EventModulesDto Modules
);

/// <summary>
/// Lightweight dashboard payload for the shell and event home. It combines the
/// current phase, member permissions, high-level counts and module visibility.
/// </summary>
public record EventOverviewDto(
    EventSummaryDto Event,
    EventPhaseDto? ActivePhase,
    EventPhaseDto? NextPhase,
    EventPermissionsDto Permissions,
    EventCountsDto Counts,
    bool HasSecretSantaDraw,
    bool HasSecretSantaAssignment,
    int MyWishlistItemCount,
    int MyProposalCount,
    int MyVoteCount,
    int VotingCategoryCount,
    EventModulesDto Modules
);

/// <summary>
/// Admin-configurable visibility flags for regular members. These toggles are
/// merged with the phase rules so admins can hide modules without hardcoding
/// that logic in the frontend.
/// </summary>
public record EventAdminModuleVisibilityDto(
    bool Feed,
    bool SecretSanta,
    bool Wishlist,
    bool Categories,
    bool Voting,
    bool Gala,
    bool Stickers,
    bool Measures,
    bool Nominees
);

/// <summary>
/// Full control-center payload for admins. It includes the active phase, all
/// available phases, legacy visibility toggles and the member-facing module
/// visibility resulting from the current configuration.
/// </summary>
public record EventAdminStateDto(
    string EventId,
    EventPhaseDto? ActivePhase,
    List<EventPhaseDto> Phases,
    bool NominationsVisible,
    bool ResultsVisible,
    EventAdminModuleVisibilityDto ModuleVisibility,
    EventModulesDto EffectiveModules,
    EventCountsDto Counts
);

/// <summary>
/// Admin-facing nominee projection enriched with submitter identity so the
/// moderation queue can render without extra joins on the client.
/// </summary>
public record AdminNomineeDto(
    string Id,
    string? CategoryId,
    string Title,
    string? ImageUrl,
    string Status,
    DateTimeOffset CreatedAtUtc,
    Guid SubmittedByUserId,
    string SubmittedByName
);

/// <summary>
/// Lightweight nomination summary for the admin moderation queue — omits
/// ImageUrl and timestamp to reduce payload size in the queue list.
/// </summary>
public record AdminNomineeSummaryDto(
    string Id,
    string? CategoryId,
    string Title,
    string Status,
    Guid SubmittedByUserId,
    string SubmittedByName
);

/// <summary>
/// Vote tally for one person/category entry in the official results screen.
/// </summary>
public record AdminNomineeVoteTallyDto(
    string NomineeId,
    string NomineeTitle,
    string? ImageUrl,
    int VoteCount,
    List<string> VoterUserIds
);

/// <summary>
/// Aggregated official result for a user-vote category.
/// </summary>
public record AdminCategoryResultDto(
    string CategoryId,
    string CategoryName,
    int TotalVotes,
    List<AdminNomineeVoteTallyDto> Nominees,
    double ParticipationRate
);

/// <summary>
/// Official-results snapshot used by the admin audit panel.
/// </summary>
public record AdminOfficialResultsDto(
    string EventId,
    DateTimeOffset GeneratedAt,
    int TotalMembers,
    List<AdminCategoryResultDto> Categories
);

/// <summary>
/// Single bootstrap payload for the admin control center. Lists are now
/// optional — the default response contains only counts so the frontend can
/// lazy-load each section on demand. Passing includeLists=true returns the
/// full lists for backward compatibility.
/// </summary>
public record EventAdminBootstrapDto(
    List<EventSummaryDto> Events,
    EventAdminStateDto State,
    AdminListCountsDto Counts,
    List<AwardCategoryDto>? Categories = null,
    List<NomineeDto>? Nominees = null,
    List<AdminNomineeDto>? AdminNominees = null,
    AdminProposalsHistoryDto? Proposals = null,
    AdminVotesDto? Votes = null,
    List<PublicUserDto>? Members = null,
    EventAdminSecretSantaStateDto? SecretSanta = null,
    AdminOfficialResultsDto? OfficialResults = null
);

/// <summary>
/// Updates admin-managed visibility flags without changing the active phase.
/// </summary>
public record UpdateEventAdminStateRequest(
    bool? NominationsVisible,
    bool? ResultsVisible,
    EventAdminModuleVisibilityDto? ModuleVisibility
);

/// <summary>
/// Switches the event to one of the known phase windows.
/// </summary>
public record UpdateEventPhaseRequest(string PhaseType);

/// <summary>
/// Summary of the member's current voting progress for the active event.
/// </summary>
public record EventVotingOverviewDto(
    string EventId,
    string? PhaseId,
    bool CanVote,
    DateTimeOffset? EndsAt,
    int CategoryCount,
    int SubmittedVoteCount,
    int RemainingVoteCount
);

/// <summary>
/// Summary of the current member's Secret Santa state, including assignment and
/// wishlist counts for both sides of the pairing when available.
/// </summary>
public record EventSecretSantaOverviewDto(
    string EventId,
    bool HasDraw,
    bool HasAssignment,
    string? DrawEventCode,
    EventUserDto? AssignedUser,
    int AssignedWishlistItemCount,
    int MyWishlistItemCount
);

/// <summary>
/// Admin-facing snapshot of the current draw state for a specific event.
/// </summary>
public record EventAdminSecretSantaStateDto(
    string EventId,
    string EventCode,
    bool HasDraw,
    string? DrawId,
    DateTimeOffset? CreatedAtUtc,
    bool IsLocked,
    int MemberCount,
    int AssignmentCount
);

/// <summary>
/// Allows the admin panel to trigger or rerun the Secret Santa draw for the
/// current event. EventCode stays optional while the legacy draw model still
/// exists alongside the event-scoped API.
/// </summary>
public record CreateEventSecretSantaDrawRequest(string? EventCode);

public record EventFeedPostDto(
    string Id,
    string EventId,
    Guid UserId,
    string UserName,
    string Content,
    string? ImageUrl,
    List<string> MediaUrls,
    DateTimeOffset CreatedAt
);

/// <summary>
/// Full-featured feed post DTO — mirrors HubPostDto with all engagement fields
/// so the event-scoped feed can replace the legacy /hub/ endpoints completely.
/// </summary>
public record EventFeedPostFullDto(
    string Id,
    string EventId,
    string AuthorUserId,
    string AuthorName,
    string Text,
    string? MediaUrl,
    List<string> MediaUrls,
    bool IsPinned,
    DateTimeOffset CreatedAtUtc,
    int LikeCount,
    int CommentCount,
    int DownvoteCount,
    Dictionary<string, int> ReactionCounts,
    List<string> MyReactions,
    bool LikedByMe,
    bool DownvotedByMe,
    EventFeedPollDto? Poll
);

public record EventFeedPollDto(
    string Question,
    List<EventFeedPollOptionDto> Options,
    string? MyOptionId,
    int TotalVotes
);

public record EventFeedPollOptionDto(
    string Id,
    string Text,
    int VoteCount
);

public record CreateEventFeedPostRequest(
    string Text,
    string? MediaUrl = null,
    List<string>? MediaUrls = null,
    string? PollQuestion = null,
    List<string>? PollOptions = null
);

public record VoteEventFeedPollRequest(string OptionId);

public record CreateEventFeedCommentRequest(string Text);

public record ToggleEventFeedReactionRequest(string? Emoji);

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
    string Name,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt
);

public record CreateEventProposalRequest(string Name, string? Description);

public record UpdateEventProposalRequest(string Status);

/// <summary>
/// Admin-facing update payload for category proposals. The public event
/// proposal contract only toggles status, but the control center also needs to
/// correct title/description before approving or archiving a proposal.
/// </summary>
public record UpdateAdminCategoryProposalRequest(
    string? Name,
    string? Description,
    string? Status
);

public record EventWishlistItemDto(
    string Id,
    Guid UserId,
    string EventId,
    string Title,
    string? Link,
    string? Notes,
    string? ImageUrl,
    DateTimeOffset UpdatedAt
);

public record CreateEventWishlistItemRequest(
    string Title,
    string? Link,
    string? Notes
);
