using System;
using System.Collections.Generic;
using System.Linq;
using Canhoes.Api.Models;
using Canhoes.Api.DTOs;

namespace Canhoes.Api.Mappers;

/// <summary>
/// Provides mapping methods from entity models to DTOs.
/// </summary>
public static class EventMappers
{
    public static EventSummaryDto ToEventSummaryDto(EventEntity entity) => 
        new(entity.Id, entity.Name, entity.IsActive);

    public static EventPhaseDto ToEventPhaseDto(EventPhaseEntity entity) => 
        new(
            entity.Id,
            entity.Type,
            new DateTimeOffset(entity.StartDateUtc, TimeSpan.Zero),
            new DateTimeOffset(entity.EndDateUtc, TimeSpan.Zero),
            entity.IsActive);

    public static EventCategoryDto ToEventCategoryDto(AwardCategoryEntity entity) => 
        new(entity.Id, entity.EventId, entity.Name, entity.Kind.ToString(), entity.IsActive, entity.Description);

    public static AwardCategoryDto ToAwardCategoryDto(AwardCategoryEntity entity) => 
        new(
            entity.Id,
            entity.Name,
            entity.SortOrder,
            entity.IsActive,
            entity.Kind.ToString(),
            entity.Description,
            entity.VoteQuestion,
            entity.VoteRules);

    public static PublicUserDto ToPublicUserDto(UserEntity entity, bool isAdmin) => 
        new(entity.Id, entity.Email, entity.DisplayName, isAdmin);

    public static NomineeDto ToNomineeDto(NomineeEntity entity) => 
        new(
            entity.Id,
            entity.CategoryId,
            entity.Title,
            entity.ImageUrl,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero));

    public static AdminNomineeDto ToAdminNomineeDto(NomineeEntity entity, UserEntity? submittedByUser) => 
        new(
            entity.Id,
            entity.CategoryId,
            entity.Title,
            entity.ImageUrl,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero),
            entity.SubmittedByUserId,
            submittedByUser != null ? (submittedByUser.DisplayName ?? submittedByUser.Email) : "Unknown");

    public static CategoryProposalDto ToCategoryProposalDto(CategoryProposalEntity entity) => 
        new(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero));

    public static MeasureProposalDto ToMeasureProposalDto(MeasureProposalEntity entity) => 
        new(
            entity.Id,
            entity.Text,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero));

    public static EventFeedPostDto ToEventFeedPostDto(HubPostEntity entity, string authorName, List<string> mediaUrls) => 
        new(
            entity.Id,
            entity.EventId,
            entity.AuthorUserId,
            authorName,
            entity.Text,
            mediaUrls.FirstOrDefault(),
            mediaUrls,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero));

    public static EventFeedPostFullDto ToEventFeedPostFullDto(
        HubPostEntity entity,
        string authorName,
        List<string> mediaUrls,
        int likeCount,
        int commentCount,
        int downvoteCount,
        Dictionary<string, int> reactionCounts,
        List<string> myReactions,
        bool likedByMe,
        bool downvotedByMe,
        EventFeedPollDto? poll) => 
        new(
            entity.Id,
            entity.EventId,
            entity.AuthorUserId.ToString(),
            authorName,
            entity.Text,
            mediaUrls.FirstOrDefault(),
            mediaUrls,
            entity.IsPinned,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero),
            likeCount,
            commentCount,
            downvoteCount,
            reactionCounts,
            myReactions,
            likedByMe,
            downvotedByMe,
            poll);

    public static EventWishlistItemDto ToEventWishlistItemDto(WishlistItemEntity entity) => 
        new(
            entity.Id,
            entity.UserId,
            entity.EventId,
            entity.Title,
            entity.Url,
            entity.Notes,
            entity.ImageUrl,
            new DateTimeOffset(entity.UpdatedAtUtc, TimeSpan.Zero));

    public static EventProposalDto ToEventProposalDto(CategoryProposalEntity entity) => 
        new(
            entity.Id,
            entity.EventId,
            entity.ProposedByUserId,
            entity.Name,
            entity.Description,
            entity.Status,
            new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero));

    public static string GetUserName(UserEntity user) => 
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName!;
}
