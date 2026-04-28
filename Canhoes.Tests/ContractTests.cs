using FluentAssertions;
using Canhoes.Api.Controllers;
using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Canhoes.Tests;

public sealed class ContractTests
{
    [Fact]
    public async Task Me_ShouldReturnUserShape()
    {
        var userId = Guid.NewGuid();
        var db = TestSupport.CreateDbContext();
        db.Users.Add(new UserEntity
        {
            Id = userId,
            ExternalId = userId.ToString("N"),
            Email = "member@example.com",
            DisplayName = "Member User",
            IsAdmin = false,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var controller = TestSupport.CreateUsersController(db, userId);
        controller.ControllerContext.HttpContext.Items["CurrentUser"] = new PublicUserDto(userId, "member@example.com", "Member User", false);
        var result = await controller.Me(CancellationToken.None);
        var me = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<MeDto>().Subject;

        me.User.Email.Should().Be("member@example.com");
        me.User.DisplayName.Should().Be("Member User");
        me.User.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Me_ShouldReturnAdminFlagForAdminUser()
    {
        var userId = Guid.NewGuid();
        var db = TestSupport.CreateDbContext();
        db.Users.Add(new UserEntity
        {
            Id = userId,
            ExternalId = userId.ToString("N"),
            Email = "admin@example.com",
            DisplayName = "Admin User",
            IsAdmin = true,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var controller = TestSupport.CreateUsersController(db, userId);
        controller.ControllerContext.HttpContext.Items["CurrentUser"] = new PublicUserDto(userId, "admin@example.com", "Admin User", true);

        var result = await controller.Me(CancellationToken.None);
        var me = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<MeDto>().Subject;

        me.User.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task EventsList_ShouldReturnSummaries()
    {
        var db = TestSupport.CreateDbContext();
        db.Events.Add(new EventEntity { Id = "event-1", Name = "Event 1", IsActive = true });
        db.Events.Add(new EventEntity { Id = "event-2", Name = "Event 2", IsActive = false });
        db.SaveChanges();

        var controller = TestSupport.CreateEventsController(db, Guid.NewGuid());
        var result = await controller.ListEvents(CancellationToken.None);
        var events = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<List<EventSummaryDto>>().Subject;

        events.Should().HaveCount(2);
    }

    [Fact]
    public async Task EventContext_ShouldReturnShape()
    {
        var userId = Guid.NewGuid();
        var db = TestSupport.CreateDbContext();
        db.Events.Add(new EventEntity { Id = "event-1", Name = "Event 1", IsActive = true });
        db.EventMembers.Add(new EventMemberEntity { EventId = "event-1", UserId = userId, Role = EventRoles.User, JoinedAtUtc = DateTime.UtcNow });
        db.Users.Add(new UserEntity { Id = userId, ExternalId = userId.ToString("N"), Email = "member@example.com", DisplayName = "Member User", IsAdmin = false, CreatedAt = DateTime.UtcNow });
        db.EventPhases.Add(new EventPhaseEntity { Id = "phase-1", EventId = "event-1", Type = EventPhaseTypes.Proposals, StartDateUtc = DateTime.UtcNow.AddDays(-1), EndDateUtc = DateTime.UtcNow.AddDays(1), IsActive = true });
        db.SaveChanges();

        var controller = TestSupport.CreateEventsController(db, userId);
        var result = await controller.GetEventContext("event-1", CancellationToken.None);
        var context = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<EventContextDto>().Subject;

        context.Event.Id.Should().Be("event-1");
    }

    [Fact]
    public async Task EventOverview_ShouldReturnPermissions()
    {
        var userId = Guid.NewGuid();
        var db = TestSupport.CreateDbContext();
        db.Events.Add(new EventEntity { Id = "event-1", Name = "Event 1", IsActive = true });
        db.EventMembers.Add(new EventMemberEntity { EventId = "event-1", UserId = userId, Role = EventRoles.User, JoinedAtUtc = DateTime.UtcNow });
        db.Users.Add(new UserEntity { Id = userId, ExternalId = userId.ToString("N"), Email = "member@example.com", DisplayName = "Member User", IsAdmin = false, CreatedAt = DateTime.UtcNow });
        db.EventPhases.Add(new EventPhaseEntity { Id = "phase-1", EventId = "event-1", Type = EventPhaseTypes.Voting, StartDateUtc = DateTime.UtcNow.AddDays(-1), EndDateUtc = DateTime.UtcNow.AddDays(1), IsActive = true });
        db.SaveChanges();

        var controller = TestSupport.CreateEventsController(db, userId);
        var result = await controller.GetEventOverview("event-1", CancellationToken.None);
        var overview = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<EventOverviewDto>().Subject;

        overview.Event.Id.Should().Be("event-1");
        overview.Permissions.IsMember.Should().BeTrue();
    }

    [Fact]
    public async Task AdminState_ShouldForbidNonAdminMembers()
    {
        var userId = Guid.NewGuid();
        await using var db = TestSupport.CreateDbContext();
        TestSupport.SeedEvent(db, "event-1", isActive: true);
        TestSupport.SeedState(db, "event-1", EventPhaseTypes.Proposals, TestSupport.BuildVisibility());
        TestSupport.SeedMember(db, "event-1", userId);
        TestSupport.SeedUser(db, userId, "member@example.com", "Member User", isAdmin: false);
        await db.SaveChangesAsync();

        var controller = TestSupport.CreateEventsController(db, userId, isAdmin: false);
        var result = await controller.GetAdminState("event-1", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Uploads_ShouldReturnNotFoundForMissingPath()
    {
        var controller = new UploadsController(TestSupport.CreateDbContext(), new FakeEnv());
        (await controller.GetUpload(null, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
    }

    private sealed class FakeEnv : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Canhoes.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
