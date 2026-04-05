using Canhoes.Api.Access;
using Canhoes.Api.Controllers;
using Canhoes.Api.Data;
using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace Canhoes.Tests;

public sealed class EventModuleAccessTests
{
    [Fact]
    public async Task EvaluateAsync_ShouldGiveAdminsFullModuleAccess()
    {
        await using var db = CreateDbContext();
        SeedEvent(db, "event-admin", isActive: true);
        SeedState(db, "event-admin", EventPhaseTypes.Draw, BuildVisibility(feed: false, wishlist: false, voting: false));

        var snapshot = await EventModuleAccessEvaluator.EvaluateAsync(
            db,
            "event-admin",
            Guid.NewGuid(),
            isAdmin: true,
            CancellationToken.None);

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
        await using var db = CreateDbContext();
        SeedEvent(db, "event-a", isActive: true);
        SeedState(db, "event-a", EventPhaseTypes.Proposals, BuildVisibility(voting: true, gala: true));
        SeedEvent(db, "event-b");
        SeedState(db, "event-b", EventPhaseTypes.Results, BuildVisibility(categories: false, gala: true));

        var eventASnapshot = await EventModuleAccessEvaluator.EvaluateAsync(
            db,
            "event-a",
            Guid.NewGuid(),
            isAdmin: false,
            CancellationToken.None);
        var eventBSnapshot = await EventModuleAccessEvaluator.EvaluateAsync(
            db,
            "event-b",
            Guid.NewGuid(),
            isAdmin: false,
            CancellationToken.None);

        eventASnapshot.EffectiveModules.Categories.Should().BeTrue();
        eventASnapshot.EffectiveModules.Stickers.Should().BeTrue();
        eventASnapshot.EffectiveModules.Measures.Should().BeTrue();
        eventASnapshot.EffectiveModules.Voting.Should().BeFalse();
        eventASnapshot.EffectiveModules.Gala.Should().BeFalse();

        eventBSnapshot.EffectiveModules.Categories.Should().BeFalse();
        eventBSnapshot.EffectiveModules.Gala.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrCreateEventStateAsync_ShouldCreateIndependentRowsPerEvent()
    {
        await using var db = CreateDbContext();
        SeedEvent(db, "event-a", isActive: true);
        SeedState(db, "event-a", EventPhaseTypes.Proposals, BuildVisibility());
        SeedEvent(db, "event-b");

        var state = await EventModuleAccessEvaluator.GetOrCreateEventStateAsync(
            db,
            "event-b",
            CancellationToken.None);

        state.EventId.Should().Be("event-b");
        db.CanhoesEventState.Select(x => x.EventId).Should().BeEquivalentTo(new[] { "event-a", "event-b" });
    }

    [Fact]
    public async Task GetState_ShouldFollowTheActiveEvent()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();

        SeedEvent(db, EventContextDefaults.DefaultEventId, isActive: false);
        SeedState(db, EventContextDefaults.DefaultEventId, EventPhaseTypes.Proposals, BuildVisibility());
        SeedEvent(db, "event-2027", isActive: true);
        SeedState(db, "event-2027", EventPhaseTypes.Results, BuildVisibility(gala: true));
        SeedMember(db, "event-2027", userId);

        var controller = CreateCanhoesController(db, userId);

        var result = await controller.GetState(CancellationToken.None);

        result.Value.Should().NotBeNull();
        result.Value!.Phase.Should().Be("gala");
    }

    [Fact]
    public async Task GetWishlist_ShouldForbidWhenModuleIsHidden()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();

        SeedEvent(db, "event-hidden", isActive: true);
        SeedState(db, "event-hidden", EventPhaseTypes.Proposals, BuildVisibility(wishlist: false));
        SeedMember(db, "event-hidden", userId);

        var controller = CreateCanhoesController(db, userId);

        var result = await controller.GetWishlist(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task AdminGetCategories_ShouldFollowTheActiveEvent()
    {
        await using var db = CreateDbContext();
        var adminId = Guid.NewGuid();

        SeedEvent(db, EventContextDefaults.DefaultEventId, isActive: false);
        SeedState(db, EventContextDefaults.DefaultEventId, EventPhaseTypes.Proposals, BuildVisibility());
        SeedCategory(db, EventContextDefaults.DefaultEventId, "Categoria antiga", sortOrder: 1);

        SeedEvent(db, "event-2027", isActive: true);
        SeedState(db, "event-2027", EventPhaseTypes.Proposals, BuildVisibility());
        SeedCategory(db, "event-2027", "Categoria ativa", sortOrder: 1);

        var controller = CreateCanhoesController(db, adminId, isAdmin: true);

        var result = await controller.AdminGetCategories(CancellationToken.None);
        var categories = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<List<AwardCategoryDto>>().Subject;

        categories.Should().ContainSingle(x => x.Name == "Categoria ativa");
        categories.Should().NotContain(x => x.Name == "Categoria antiga");
    }

    [Fact]
    public async Task GetNominees_ShouldApplyKindSpecificModuleGuards()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();

        SeedEvent(db, "event-nominees", isActive: true);
        SeedState(
            db,
            "event-nominees",
            EventPhaseTypes.Proposals,
            BuildVisibility(nominees: true, stickers: false));
        SeedMember(db, "event-nominees", userId);
        db.Nominees.Add(new NomineeEntity
        {
            Id = "nom-1",
            EventId = "event-nominees",
            CategoryId = null,
            Title = "Nomeacao aberta",
            SubmissionKind = "nominees",
            SubmittedByUserId = userId,
            Status = "pending",
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var controller = CreateCanhoesController(db, userId);

        var nomineesResult = await controller.GetNominees(null, "nominees", CancellationToken.None);
        var stickersResult = await controller.GetNominees(null, "stickers", CancellationToken.None);

        nomineesResult.Value.Should().ContainSingle(x => x.Title == "Nomeacao aberta");
        stickersResult.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task AdminGetProposalsHistory_ShouldGroupStatusesPerEvent()
    {
        await using var db = CreateDbContext();
        var adminId = Guid.NewGuid();

        SeedEvent(db, "event-history", isActive: true);
        SeedState(db, "event-history", EventPhaseTypes.Proposals, BuildVisibility());
        SeedEvent(db, "event-other");
        SeedState(db, "event-other", EventPhaseTypes.Proposals, BuildVisibility());

        db.CategoryProposals.AddRange(
            new CategoryProposalEntity
            {
                Id = "cat-pending",
                EventId = "event-history",
                Name = "Categoria Pendente",
                Status = "pending",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-3)
            },
            new CategoryProposalEntity
            {
                Id = "cat-approved",
                EventId = "event-history",
                Name = "Categoria Aprovada",
                Status = "approved",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2)
            },
            new CategoryProposalEntity
            {
                Id = "cat-ignored",
                EventId = "event-other",
                Name = "Outra Categoria",
                Status = "pending",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1)
            });

        db.MeasureProposals.AddRange(
            new MeasureProposalEntity
            {
                Id = "measure-rejected",
                EventId = "event-history",
                Text = "Medida Rejeitada",
                Status = "rejected",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2)
            },
            new MeasureProposalEntity
            {
                Id = "measure-pending",
                EventId = "event-history",
                Text = "Medida Pendente",
                Status = "pending",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1)
            });
        db.SaveChanges();

        var controller = CreateEventsController(db, adminId, isAdmin: true);

        var result = await controller.AdminGetProposalsHistory("event-history", CancellationToken.None);
        var payload = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<AdminProposalsHistoryDto>().Subject;

        payload.CategoryProposals.Pending.Select(x => x.Id).Should().Equal("cat-pending");
        payload.CategoryProposals.Approved.Select(x => x.Id).Should().Equal("cat-approved");
        payload.CategoryProposals.Rejected.Should().BeEmpty();
        payload.MeasureProposals.Pending.Select(x => x.Id).Should().Equal("measure-pending");
        payload.MeasureProposals.Rejected.Select(x => x.Id).Should().Equal("measure-rejected");
    }

    [Fact]
    public async Task HubGetPosts_ShouldComposeMediaReactionsPollAndCounts()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        SeedEvent("event-feed", db, isActive: true);
        SeedState(db, "event-feed", EventPhaseTypes.Proposals, BuildVisibility());
        SeedMember(db, "event-feed", userId);
        SeedUser(db, userId, "autor@example.com", "Autor");
        SeedUser(db, otherUserId, "outro@example.com", "Outro");

        db.HubPosts.Add(new HubPostEntity
        {
            Id = "post-1",
            EventId = "event-feed",
            AuthorUserId = userId,
            Text = "Post principal",
            MediaUrl = null,
            MediaUrlsJson = null,
            IsPinned = false,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.HubPostMedia.Add(new HubPostMediaEntity
        {
            Id = "media-1",
            PostId = "post-1",
            Url = "/uploads/hub/post-1.png",
            OriginalFileName = "post-1.png",
            ContentBytes = Array.Empty<byte>(),
            UploadedAtUtc = DateTime.UtcNow
        });
        db.HubPostComments.Add(new HubPostCommentEntity
        {
            Id = "comment-1",
            PostId = "post-1",
            UserId = otherUserId,
            Text = "Comentario",
            CreatedAtUtc = DateTime.UtcNow
        });
        db.HubPostReactions.AddRange(
            new HubPostReactionEntity
            {
                Id = "reaction-1",
                PostId = "post-1",
                UserId = userId,
                Emoji = "\u2764\uFE0F",
                CreatedAtUtc = DateTime.UtcNow
            },
            new HubPostReactionEntity
            {
                Id = "reaction-2",
                PostId = "post-1",
                UserId = otherUserId,
                Emoji = "🔥",
                CreatedAtUtc = DateTime.UtcNow
            });
        db.HubPostPolls.Add(new HubPostPollEntity
        {
            PostId = "post-1",
            Question = "Qual escolhes?",
            CreatedAtUtc = DateTime.UtcNow
        });
        db.HubPostPollOptions.AddRange(
            new HubPostPollOptionEntity
            {
                Id = "opt-1",
                PostId = "post-1",
                Text = "Opcao A",
                SortOrder = 0
            },
            new HubPostPollOptionEntity
            {
                Id = "opt-2",
                PostId = "post-1",
                Text = "Opcao B",
                SortOrder = 1
            });
        db.HubPostPollVotes.AddRange(
            new HubPostPollVoteEntity
            {
                Id = "vote-1",
                PostId = "post-1",
                UserId = userId,
                OptionId = "opt-1",
                CreatedAtUtc = DateTime.UtcNow
            },
            new HubPostPollVoteEntity
            {
                Id = "vote-2",
                PostId = "post-1",
                UserId = otherUserId,
                OptionId = "opt-2",
                CreatedAtUtc = DateTime.UtcNow
            });
        db.SaveChanges();

        var controller = CreateHubController(db, userId);

        var result = await controller.GetPosts(50, CancellationToken.None);
        var posts = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<List<HubPostDto>>().Subject;
        var post = posts.Should().ContainSingle().Subject;

        post.AuthorName.Should().Be("Autor");
        post.MediaUrls.Should().Contain("/uploads/hub/post-1.png");
        post.CommentCount.Should().Be(1);
        post.ReactionCounts.Should().ContainKey("\u2764\uFE0F").WhoseValue.Should().Be(1);
        post.ReactionCounts.Should().ContainKey("🔥").WhoseValue.Should().Be(1);
        post.MyReactions.Should().Contain("\u2764\uFE0F");
        post.LikedByMe.Should().BeTrue();
        post.Poll.Should().NotBeNull();
        post.Poll!.MyOptionId.Should().Be("opt-1");
        post.Poll.TotalVotes.Should().Be(2);
    }

    private static CanhoesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CanhoesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CanhoesDbContext(options);
    }

    private static CanhoesController CreateCanhoesController(CanhoesDbContext db, Guid userId, bool isAdmin = false)
    {
        var controller = new CanhoesController(db, new FakeWebHostEnvironment())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.ControllerContext.HttpContext.Items["UserId"] = userId;
        controller.ControllerContext.HttpContext.Items["IsAdmin"] = isAdmin;
        return controller;
    }

    private static EventsController CreateEventsController(CanhoesDbContext db, Guid userId, bool isAdmin = false)
    {
        var controller = new EventsController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.ControllerContext.HttpContext.Items["UserId"] = userId;
        controller.ControllerContext.HttpContext.Items["IsAdmin"] = isAdmin;
        return controller;
    }

    private static HubController CreateHubController(CanhoesDbContext db, Guid userId, bool isAdmin = false)
    {
        var controller = new HubController(db, new FakeWebHostEnvironment())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.ControllerContext.HttpContext.Items["UserId"] = userId;
        controller.ControllerContext.HttpContext.Items["IsAdmin"] = isAdmin;
        return controller;
    }

    private static void SeedEvent(CanhoesDbContext db, string eventId, bool isActive = false)
    {
        db.Events.Add(new EventEntity
        {
            Id = eventId,
            Name = eventId,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private static void SeedEvent(string eventId, CanhoesDbContext db, bool isActive = false) =>
        SeedEvent(db, eventId, isActive);

    private static void SeedMember(CanhoesDbContext db, string eventId, Guid userId)
    {
        db.EventMembers.Add(new EventMemberEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            UserId = userId,
            Role = EventRoles.User,
            JoinedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private static void SeedCategory(CanhoesDbContext db, string eventId, string name, int sortOrder)
    {
        db.AwardCategories.Add(new AwardCategoryEntity
        {
            Id = Guid.NewGuid().ToString(),
            EventId = eventId,
            Name = name,
            SortOrder = sortOrder,
            Kind = AwardCategoryKind.Sticker,
            IsActive = true
        });
        db.SaveChanges();
    }

    private static void SeedUser(CanhoesDbContext db, Guid userId, string email, string? displayName = null, bool isAdmin = false)
    {
        db.Users.Add(new UserEntity
        {
            Id = userId,
            ExternalId = $"ext-{userId:N}",
            Email = email,
            DisplayName = displayName,
            IsAdmin = isAdmin,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private static void SeedState(
        CanhoesDbContext db,
        string eventId,
        string activePhaseType,
        EventAdminModuleVisibilityDto visibility)
    {
        if (!db.EventPhases.Any(x => x.EventId == eventId))
        {
            db.EventPhases.AddRange(
                new EventPhaseEntity
                {
                    Id = $"{eventId}-draw",
                    EventId = eventId,
                    Type = EventPhaseTypes.Draw,
                    StartDateUtc = DateTime.UtcNow.AddDays(-30),
                    EndDateUtc = DateTime.UtcNow.AddDays(30),
                    IsActive = activePhaseType == EventPhaseTypes.Draw
                },
                new EventPhaseEntity
                {
                    Id = $"{eventId}-proposals",
                    EventId = eventId,
                    Type = EventPhaseTypes.Proposals,
                    StartDateUtc = DateTime.UtcNow.AddDays(-30),
                    EndDateUtc = DateTime.UtcNow.AddDays(30),
                    IsActive = activePhaseType == EventPhaseTypes.Proposals
                },
                new EventPhaseEntity
                {
                    Id = $"{eventId}-voting",
                    EventId = eventId,
                    Type = EventPhaseTypes.Voting,
                    StartDateUtc = DateTime.UtcNow.AddDays(-30),
                    EndDateUtc = DateTime.UtcNow.AddDays(30),
                    IsActive = activePhaseType == EventPhaseTypes.Voting
                },
                new EventPhaseEntity
                {
                    Id = $"{eventId}-results",
                    EventId = eventId,
                    Type = EventPhaseTypes.Results,
                    StartDateUtc = DateTime.UtcNow.AddDays(-30),
                    EndDateUtc = DateTime.UtcNow.AddDays(30),
                    IsActive = activePhaseType == EventPhaseTypes.Results
                });
        }

        var state = new CanhoesEventStateEntity
        {
            Id = (db.CanhoesEventState.Max(x => (int?)x.Id) ?? 0) + 1,
            EventId = eventId,
            Phase = activePhaseType switch
            {
                EventPhaseTypes.Proposals => "nominations",
                EventPhaseTypes.Voting => "voting",
                EventPhaseTypes.Results => "gala",
                _ => "locked",
            },
            NominationsVisible = activePhaseType == EventPhaseTypes.Proposals,
            ResultsVisible = activePhaseType == EventPhaseTypes.Results,
            ModuleVisibilityJson = EventModuleAccessEvaluator.SerializeModuleVisibility(visibility)
        };

        db.CanhoesEventState.Add(state);
        db.SaveChanges();
    }

    private static EventAdminModuleVisibilityDto BuildVisibility(
        bool feed = true,
        bool secretSanta = true,
        bool wishlist = true,
        bool categories = true,
        bool voting = true,
        bool gala = true,
        bool stickers = true,
        bool measures = true,
        bool nominees = true)
    {
        return new EventAdminModuleVisibilityDto(
            feed,
            secretSanta,
            wishlist,
            categories,
            voting,
            gala,
            stickers,
            measures,
            nominees);
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Canhoes.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
