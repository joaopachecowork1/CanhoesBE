using Canhoes.Api.Access;
using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
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

}

