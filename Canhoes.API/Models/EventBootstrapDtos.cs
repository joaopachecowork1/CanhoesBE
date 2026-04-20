namespace Canhoes.Api.Models;

public sealed record EventActiveContextDto(
    EventSummaryDto Event,
    EventOverviewDto Overview);

public sealed record EventHomeSnapshotDto(
    EventSummaryDto Event,
    EventOverviewDto Overview,
    EventVotingOverviewDto Voting,
    EventSecretSantaOverviewDto SecretSanta,
    List<EventFeedPostFullDto> RecentPosts);
