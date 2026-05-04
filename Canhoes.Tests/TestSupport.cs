using Canhoes.Api.Access;
using Canhoes.Api.Controllers;
using Canhoes.Api.Data;
using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using Canhoes.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Canhoes.Tests;

internal static class TestSupport
{
    public static CanhoesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CanhoesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CanhoesDbContext(options);
    }

    public static EventsController CreateEventsController(CanhoesDbContext db, Guid userId, bool isAdmin = false)
    {
        var controller = new EventsController(
            db,
            env: null,
            secretSanta: new SecretSantaService(),
            cache: new MemoryCache(new MemoryCacheOptions()),
            hub: new TestControllerFactories.NoopHubContext())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ControllerContext.HttpContext.Items["UserId"] = userId;
        controller.ControllerContext.HttpContext.Items["IsAdmin"] = isAdmin;
        return controller;
    }

    public static UsersController CreateUsersController(CanhoesDbContext db, Guid userId)
    {
        var controller = new UsersController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ControllerContext.HttpContext.Items["UserId"] = userId;
        controller.ControllerContext.HttpContext.Items["IsAdmin"] = false;
        return controller;
    }

    public static void SeedEvent(CanhoesDbContext db, string eventId, bool isActive = false)
        => db.Events.Add(new EventEntity { Id = eventId, Name = eventId, IsActive = isActive, CreatedAtUtc = DateTime.UtcNow });

    public static void SeedMember(CanhoesDbContext db, string eventId, Guid userId)
        => db.EventMembers.Add(new EventMemberEntity { Id = Guid.NewGuid().ToString(), EventId = eventId, UserId = userId, Role = EventRoles.User, JoinedAtUtc = DateTime.UtcNow });

    public static void SeedUser(CanhoesDbContext db, Guid userId, string email, string? displayName = null, bool isAdmin = false)
        => db.Users.Add(new UserEntity { Id = userId, ExternalId = $"ext-{userId:N}", Email = email, DisplayName = displayName, IsAdmin = isAdmin, CreatedAt = DateTime.UtcNow });

    public static void SeedState(CanhoesDbContext db, string eventId, string activePhaseType, EventAdminModuleVisibilityDto visibility)
    {
        db.EventPhases.AddRange(
            new EventPhaseEntity { Id = $"{eventId}-draw", EventId = eventId, Type = EventPhaseTypes.Draw, StartDateUtc = DateTime.UtcNow.AddDays(-30), EndDateUtc = DateTime.UtcNow.AddDays(30), IsActive = activePhaseType == EventPhaseTypes.Draw },
            new EventPhaseEntity { Id = $"{eventId}-proposals", EventId = eventId, Type = EventPhaseTypes.Proposals, StartDateUtc = DateTime.UtcNow.AddDays(-30), EndDateUtc = DateTime.UtcNow.AddDays(30), IsActive = activePhaseType == EventPhaseTypes.Proposals },
            new EventPhaseEntity { Id = $"{eventId}-voting", EventId = eventId, Type = EventPhaseTypes.Voting, StartDateUtc = DateTime.UtcNow.AddDays(-30), EndDateUtc = DateTime.UtcNow.AddDays(30), IsActive = activePhaseType == EventPhaseTypes.Voting },
            new EventPhaseEntity { Id = $"{eventId}-results", EventId = eventId, Type = EventPhaseTypes.Results, StartDateUtc = DateTime.UtcNow.AddDays(-30), EndDateUtc = DateTime.UtcNow.AddDays(30), IsActive = activePhaseType == EventPhaseTypes.Results });

        db.CanhoesEventState.Add(new CanhoesEventStateEntity
        {
            Id = eventId.GetHashCode(),
            EventId = eventId,
            Phase = activePhaseType,
            NominationsVisible = activePhaseType == EventPhaseTypes.Proposals,
            ResultsVisible = activePhaseType == EventPhaseTypes.Results,
            ModuleVisibilityJson = EventModuleAccessEvaluator.SerializeModuleVisibility(visibility)
        });
    }

    public static EventAdminModuleVisibilityDto BuildVisibility(bool feed = true, bool secretSanta = true, bool wishlist = true, bool categories = true, bool voting = true, bool gala = true, bool stickers = true, bool measures = true, bool nominees = true)
        => new(feed, secretSanta, wishlist, categories, voting, gala, stickers, measures, nominees);
}
