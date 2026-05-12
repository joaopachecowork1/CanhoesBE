using Canhoes.Api.Controllers;
using Canhoes.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Canhoes.Tests;

internal static class ControllerHelpers
{
    public static EventsController CreateEventsController(CanhoesDbContext db, Guid userId, bool isAdmin = false)
    {
        var userRepository = new Canhoes.Api.Repositories.UserRepository(db);
        var eventRepository = new Canhoes.Api.Repositories.EventRepository(db);
        var awardRepository = new Canhoes.Api.Repositories.AwardRepository(db);
        var secretSantaRepository = new Canhoes.Api.Repositories.SecretSantaRepository(db);
        
        var moduleAccessService = new Canhoes.Api.Access.ModuleAccessService(eventRepository, secretSantaRepository);
        var feedRepository = new Canhoes.Api.Repositories.FeedRepository(db);
        var feedService = new Canhoes.Api.Services.FeedService(feedRepository, userRepository);
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var awardService = new Canhoes.Api.Services.AwardService(awardRepository, userRepository, eventRepository);
        var eventService = new Canhoes.Api.Services.EventService(eventRepository, userRepository, awardRepository, secretSantaRepository, moduleAccessService, feedService, cache);
        var secretSantaService = new Canhoes.Api.Services.SecretSantaService(secretSantaRepository, userRepository, eventRepository);

        var controller = new EventsController(eventService, awardService, secretSantaService, db, cache: cache)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ControllerContext.HttpContext.Items["UserId"] = userId;
        controller.ControllerContext.HttpContext.Items["IsAdmin"] = isAdmin;
        return controller;
    }

    public static UsersController CreateUsersController(CanhoesDbContext db, Guid userId)
    {
        var userRepository = new Canhoes.Api.Repositories.UserRepository(db);
        var controller = new UsersController(userRepository)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ControllerContext.HttpContext.Items["UserId"] = userId;
        controller.ControllerContext.HttpContext.Items["IsAdmin"] = false;
        return controller;
    }
}
