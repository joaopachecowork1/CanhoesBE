using Canhoes.Api.Models;

namespace Canhoes.Api.Controllers;

public partial class CanhoesController
{
    private static AwardCategoryDto ToAwardCategoryDto(AwardCategoryEntity c) =>
        new(c.Id, c.Name, c.SortOrder, c.IsActive, c.Kind.ToString(), c.Description, c.VoteQuestion, c.VoteRules);

    private static CategoryProposalDto ToCategoryProposalDto(CategoryProposalEntity p) =>
        new(p.Id, p.Name, p.Description, p.Status, new DateTimeOffset(p.CreatedAtUtc, TimeSpan.Zero));

    private static GalaMeasureDto ToGalaMeasureDto(GalaMeasureEntity measure) =>
        new(measure.Id, measure.Text, measure.IsActive, new DateTimeOffset(measure.CreatedAtUtc, TimeSpan.Zero));

    private static MeasureProposalDto ToMeasureProposalDto(MeasureProposalEntity p) =>
        new(p.Id, p.Text, p.Status, new DateTimeOffset(p.CreatedAtUtc, TimeSpan.Zero));

    private static NomineeDto ToNomineeDto(NomineeEntity nominee) =>
        new(nominee.Id, nominee.CategoryId, nominee.Title, nominee.ImageUrl, nominee.Status, new DateTimeOffset(nominee.CreatedAtUtc, TimeSpan.Zero));

    private static PublicUserDto ToPublicUserDto(UserEntity user) =>
        new(user.Id, user.Email, user.DisplayName, user.IsAdmin);

    private static SecretSantaDrawDto ToSecretSantaDrawDto(SecretSantaDrawEntity draw) =>
        new(draw.Id, draw.EventCode, new DateTimeOffset(draw.CreatedAtUtc, TimeSpan.Zero), draw.IsLocked);

    private static UserVoteDto ToUserVoteDto(UserVoteEntity vote) =>
        new(vote.Id, vote.CategoryId, vote.VoterUserId, vote.TargetUserId, new DateTimeOffset(vote.UpdatedAtUtc, TimeSpan.Zero));

    private static VoteDto ToVoteDto(VoteEntity vote) =>
        new(vote.Id, vote.CategoryId, vote.NomineeId, vote.UserId, vote.UpdatedAtUtc);

    private static WishlistItemDto ToWishlistItemDto(WishlistItemEntity item) =>
        new(item.Id, item.UserId, item.Title, item.Url, item.Notes, item.ImageUrl, new DateTimeOffset(item.CreatedAtUtc, TimeSpan.Zero), new DateTimeOffset(item.UpdatedAtUtc, TimeSpan.Zero));
}
