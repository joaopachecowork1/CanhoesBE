namespace Canhoes.Api.Models;

/// <summary>
/// Standardized proposal status values used across category and measure proposals.
/// </summary>
public static class ProposalStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static bool IsValid(string status) =>
        status is Pending or Approved or Rejected;
}

/// <summary>
/// Standardized legacy phase names used by the CanhoesEventState.Phase column.
/// These map to EventPhaseTypes but are stored as lowercase strings for backward
/// compatibility with older database rows.
/// </summary>
public static class LegacyPhaseNames
{
    public const string Nominations = "nominations";
    public const string Voting = "voting";
    public const string Gala = "gala";
    public const string Locked = "locked";
}
