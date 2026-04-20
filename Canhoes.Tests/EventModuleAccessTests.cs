using Canhoes.Api.Access;
using Canhoes.Api.Controllers;
using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Reflection;
using Xunit;

namespace Canhoes.Tests;

public sealed class EventModuleAccessTests
{
    [Fact]
    public async Task EvaluateAsync_ShouldGiveAdminsFullModuleAccess()
    {
        await using var db = TestSupport.CreateDbContext();
        TestSupport.SeedEvent(db, "event-admin", isActive: true);
        TestSupport.SeedState(db, "event-admin", EventPhaseTypes.Draw, TestSupport.BuildVisibility(feed: false, wishlist: false, voting: false));

        var snapshot = await EventModuleAccessEvaluator.EvaluateAsync(db, "event-admin", Guid.NewGuid(), isAdmin: true, CancellationToken.None);

        snapshot.EffectiveModules.Feed.Should().BeTrue();
        snapshot.EffectiveModules.SecretSanta.Should().BeTrue();
        snapshot.EffectiveModules.Wishlist.Should().BeTrue();
        snapshot.EffectiveModules.Categories.Should().BeTrue();
        snapshot.EffectiveModules.Voting.Should().BeTrue();
        snapshot.EffectiveModules.Gala.Should().BeTrue();
        snapshot.EffectiveModules.Stickers.Should().BeTrue();
        snapshot.EffectiveModules.Measures.Should().BeTrue();
        snapshot.EffectiveModules.Nominees.Should().BeTrue();
        snapshot.EffectiveModules.Admin.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_ShouldRespectPhaseAndPerEventVisibilityForMembers()
    {
        await using var db = TestSupport.CreateDbContext();
        TestSupport.SeedEvent(db, "event-a", isActive: true);
        TestSupport.SeedState(db, "event-a", EventPhaseTypes.Proposals, TestSupport.BuildVisibility(voting: true, gala: true));
        TestSupport.SeedEvent(db, "event-b");
        TestSupport.SeedState(db, "event-b", EventPhaseTypes.Results, TestSupport.BuildVisibility(categories: false, gala: true));

        var eventASnapshot = await EventModuleAccessEvaluator.EvaluateAsync(db, "event-a", Guid.NewGuid(), isAdmin: false, CancellationToken.None);
        var eventBSnapshot = await EventModuleAccessEvaluator.EvaluateAsync(db, "event-b", Guid.NewGuid(), isAdmin: false, CancellationToken.None);

        eventASnapshot.EffectiveModules.Categories.Should().BeTrue();
        eventASnapshot.EffectiveModules.Stickers.Should().BeTrue();
        eventASnapshot.EffectiveModules.Measures.Should().BeTrue();
        eventASnapshot.EffectiveModules.Voting.Should().BeFalse();
        eventASnapshot.EffectiveModules.Gala.Should().BeFalse();

        eventBSnapshot.EffectiveModules.Categories.Should().BeFalse();
        eventBSnapshot.EffectiveModules.Gala.Should().BeTrue();
    }

    [Theory]
    [InlineData(EventPhaseTypes.Draw, true, true, true, false, false, true, true, true)]
    [InlineData(EventPhaseTypes.Proposals, false, true, true, false, false, true, true, true)]
    [InlineData(EventPhaseTypes.Voting, true, true, true, true, false, true, true, true)]
    [InlineData(EventPhaseTypes.Results, true, true, true, false, true, true, true, true)]
    public async Task EvaluateAsync_ShouldApplyCurrentMemberPhaseMatrix(
        string activePhaseType,
        bool secretSantaVisible,
        bool wishlistVisible,
        bool categoriesVisible,
        bool votingVisible,
        bool galaVisible,
        bool stickersVisible,
        bool measuresVisible,
        bool nomineesVisible)
    {
        await using var db = TestSupport.CreateDbContext();
        TestSupport.SeedEvent(db, "event-phase-matrix", isActive: true);
        TestSupport.SeedState(db, "event-phase-matrix", activePhaseType, TestSupport.BuildVisibility());

        var snapshot = await EventModuleAccessEvaluator.EvaluateAsync(db, "event-phase-matrix", Guid.NewGuid(), isAdmin: false, CancellationToken.None);

        snapshot.EffectiveModules.Feed.Should().BeTrue();
        snapshot.EffectiveModules.SecretSanta.Should().Be(secretSantaVisible);
        snapshot.EffectiveModules.Wishlist.Should().Be(wishlistVisible);
        snapshot.EffectiveModules.Categories.Should().Be(categoriesVisible);
        snapshot.EffectiveModules.Voting.Should().Be(votingVisible);
        snapshot.EffectiveModules.Gala.Should().Be(galaVisible);
        snapshot.EffectiveModules.Stickers.Should().Be(true);
        snapshot.EffectiveModules.Measures.Should().Be(true);
        snapshot.EffectiveModules.Nominees.Should().Be(nomineesVisible);
        snapshot.EffectiveModules.Admin.Should().BeFalse();
    }

    [Fact]
    public async Task GetAdminState_ShouldExposeMemberFacingEffectiveModules()
    {
        await using var db = TestSupport.CreateDbContext();
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        TestSupport.SeedEvent(db, "event-preview", isActive: true);
        TestSupport.SeedState(db, "event-preview", EventPhaseTypes.Proposals, TestSupport.BuildVisibility(secretSanta: false, wishlist: false, voting: true));
        TestSupport.SeedMember(db, "event-preview", memberId);
        TestSupport.SeedUser(db, adminId, "admin@example.com", "Admin", isAdmin: true);
        TestSupport.SeedMember(db, "event-preview", adminId);

        var adminController = TestSupport.CreateEventsController(db, adminId, isAdmin: true);
        var memberController = TestSupport.CreateEventsController(db, memberId, isAdmin: false);

        var adminStateResult = await adminController.GetAdminState("event-preview", CancellationToken.None);
        adminStateResult.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetAdminState_ShouldKeepSecretSantaPreviewVisibleWhenDrawExists()
    {
        await using var db = TestSupport.CreateDbContext();
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        TestSupport.SeedEvent(db, "event-secret-santa", isActive: true);
        TestSupport.SeedState(db, "event-secret-santa", EventPhaseTypes.Proposals, TestSupport.BuildVisibility());
        TestSupport.SeedMember(db, "event-secret-santa", memberId);
        db.SecretSantaDraws.Add(new SecretSantaDrawEntity { Id = "draw-1", EventCode = "event-secret-santa", CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = adminId, IsLocked = true });
        db.SaveChanges();

        var adminController = TestSupport.CreateEventsController(db, adminId, isAdmin: true);
        var memberController = TestSupport.CreateEventsController(db, memberId, isAdmin: false);

        var adminStateResult = await adminController.GetAdminState("event-secret-santa", CancellationToken.None);
        var adminState = adminStateResult.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<EventAdminStateDto>().Subject;
        var overviewResult = await memberController.GetEventOverview("event-secret-santa", CancellationToken.None);
        var overview = overviewResult.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<EventOverviewDto>().Subject;

        adminState.EffectiveModules.SecretSanta.Should().BeTrue();
        adminState.EffectiveModules.Should().BeEquivalentTo(overview.Modules);
    }

    [Fact]
    public void LegacyAdminRoutes_ShouldNotBeExposed()
    {
        static IEnumerable<string> GetHttpTemplates(Type controllerType) =>
            controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true))
                .SelectMany(attribute => attribute.Template is null ? [] : new[] { attribute.Template });

        var eventRoutes = GetHttpTemplates(typeof(EventsController)).ToList();
        var legacyRoutes = GetHttpTemplates(typeof(CanhoesController)).ToList();

        eventRoutes.Should().NotContain(template => template.Contains("admin/nominees", StringComparison.OrdinalIgnoreCase) && !template.Contains("summary", StringComparison.OrdinalIgnoreCase) || template.Contains("admin/votes", StringComparison.OrdinalIgnoreCase) && !template.Contains("paged", StringComparison.OrdinalIgnoreCase) || template.Contains("admin/members", StringComparison.OrdinalIgnoreCase) && !template.Contains("paged", StringComparison.OrdinalIgnoreCase) || template.Contains("admin/official-results", StringComparison.OrdinalIgnoreCase) && !template.Contains("paged", StringComparison.OrdinalIgnoreCase) || template.Contains("admin/proposals", StringComparison.OrdinalIgnoreCase));
        legacyRoutes.Should().NotContain(template => template.StartsWith("admin/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HubGetPosts_ShouldComposeMediaReactionsPollAndCounts()
    {
        await using var db = TestSupport.CreateDbContext();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        TestSupport.SeedEvent(db, "event-feed", isActive: true);
        TestSupport.SeedState(db, "event-feed", EventPhaseTypes.Proposals, TestSupport.BuildVisibility());
        TestSupport.SeedMember(db, "event-feed", userId);
        TestSupport.SeedUser(db, userId, "autor@example.com", "Autor");
        TestSupport.SeedUser(db, otherUserId, "outro@example.com", "Outro");

        db.HubPosts.Add(new HubPostEntity { Id = "post-1", EventId = "event-feed", AuthorUserId = userId, Text = "Post principal", MediaUrl = null, MediaUrlsJson = "[]", IsPinned = false, CreatedAtUtc = DateTime.UtcNow });
        db.HubPostMedia.Add(new HubPostMediaEntity { Id = "media-1", PostId = "post-1", Url = "/uploads/hub/post-1.png", OriginalFileName = "post-1.png", UploadedAtUtc = DateTime.UtcNow });
        db.HubPostComments.Add(new HubPostCommentEntity { Id = "comment-1", PostId = "post-1", UserId = otherUserId, Text = "Comentario", CreatedAtUtc = DateTime.UtcNow });
        db.HubPostReactions.AddRange(new HubPostReactionEntity { Id = "reaction-1", PostId = "post-1", UserId = userId, Emoji = "❤️", CreatedAtUtc = DateTime.UtcNow }, new HubPostReactionEntity { Id = "reaction-2", PostId = "post-1", UserId = otherUserId, Emoji = "🔥", CreatedAtUtc = DateTime.UtcNow });
        db.HubPostPolls.Add(new HubPostPollEntity { PostId = "post-1", Question = "Qual escolhes?", CreatedAtUtc = DateTime.UtcNow });
        db.HubPostPollOptions.AddRange(new HubPostPollOptionEntity { Id = "opt-1", PostId = "post-1", Text = "Opcao A", SortOrder = 0 }, new HubPostPollOptionEntity { Id = "opt-2", PostId = "post-1", Text = "Opcao B", SortOrder = 1 });
        db.HubPostPollVotes.AddRange(new HubPostPollVoteEntity { Id = "vote-1", PostId = "post-1", UserId = userId, OptionId = "opt-1", CreatedAtUtc = DateTime.UtcNow }, new HubPostPollVoteEntity { Id = "vote-2", PostId = "post-1", UserId = otherUserId, OptionId = "opt-2", CreatedAtUtc = DateTime.UtcNow });
        db.SaveChanges();

        var controller = TestSupport.CreateHubController(db, userId);
        var result = await controller.GetPosts(50, CancellationToken.None);
        var posts = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<List<HubPostDto>>().Subject;
        var post = posts.Should().ContainSingle().Subject;

        post.AuthorName.Should().Be("Autor");
        post.MediaUrls.Should().Contain("/uploads/hub/post-1.png");
        post.CommentCount.Should().Be(1);
        post.ReactionCounts.Should().ContainKey("❤️").WhoseValue.Should().Be(1);
        post.ReactionCounts.Should().ContainKey("🔥").WhoseValue.Should().Be(1);
        post.MyReactions.Should().Contain("❤️");
        post.LikedByMe.Should().BeTrue();
        post.Poll.Should().NotBeNull();
        post.Poll!.MyOptionId.Should().Be("opt-1");
        post.Poll.TotalVotes.Should().Be(2);
    }
}
