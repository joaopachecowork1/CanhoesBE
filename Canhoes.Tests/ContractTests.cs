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
        var result = await controller.Me(CancellationToken.None);
        var me = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<MeDto>().Subject;

        me.User.Email.Should().Be("member@example.com");
        me.User.DisplayName.Should().Be("Member User");
    }

    [Fact]
    public async Task Users_ShouldReturnPagedShape()
    {
        var db = TestSupport.CreateDbContext();
        db.Users.Add(new UserEntity { Id = Guid.NewGuid(), ExternalId = "a", Email = "a@example.com", DisplayName = "Alice", IsAdmin = true, CreatedAt = DateTime.UtcNow });
        db.Users.Add(new UserEntity { Id = Guid.NewGuid(), ExternalId = "b", Email = "b@example.com", DisplayName = "Bob", IsAdmin = false, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();

        var controller = TestSupport.CreateUsersController(db, Guid.NewGuid());
        var result = await controller.ListUsers(skip: 0, take: 1, CancellationToken.None);
        var page = result.Value.Should().BeOfType<PagedResult<PublicUserDto>>().Subject;

        page.Items.Should().HaveCount(1);
        page.Total.Should().Be(2);
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
