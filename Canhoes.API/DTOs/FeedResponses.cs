using System.Collections.Generic;

namespace Canhoes.Api.DTOs;

/// <summary>
/// Response for toggling a like on a feed post.
/// </summary>
public record ToggleLikeResponse(bool Liked, bool RemovedDownvote);

/// <summary>
/// Response for toggling a downvote on a feed post.
/// </summary>
public record ToggleDownvoteResponse(bool Downvoted, bool RemovedLike);

/// <summary>
/// Generic response for toggling an emoji reaction.
/// </summary>
public record ToggleReactionResponse(string Emoji, bool Active);

/// <summary>
/// Response for pinning/unpinning a post.
/// </summary>
public record TogglePinResponse(bool Pinned);

/// <summary>
/// Response for uploading files.
/// </summary>
public record UploadedFileDto(string Url, long SizeBytes, string? ContentType);

/// <summary>
/// Response wrapper for a list of uploaded files.
/// </summary>
public record UploadResultDto(IReadOnlyList<UploadedFileDto> Files);
