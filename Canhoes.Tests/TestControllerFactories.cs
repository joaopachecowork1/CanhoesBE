using Canhoes.Api.Controllers;
using Canhoes.Api.Data;
using Canhoes.Api.Hubs;
using Canhoes.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

namespace Canhoes.Tests;

internal static class TestControllerFactories
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
        var cache = new MemoryCache(new MemoryCacheOptions());
        var eventService = new Canhoes.Api.Services.EventService(eventRepository, userRepository, awardRepository, secretSantaRepository, moduleAccessService, feedService, cache);
        var awardService = new Canhoes.Api.Services.AwardService(awardRepository, userRepository, eventRepository);
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

    internal sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Canhoes.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    internal sealed class NoopHubContext : IHubContext<EventHub>
    {
        public IHubClients Clients => NoopHubClients.Instance;
        public IGroupManager Groups => NoopGroupManager.Instance;
    }

    private sealed class NoopHubClients : IHubClients
    {
        public static readonly NoopHubClients Instance = new();
        public IClientProxy All => NoopClientProxy.Instance;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => NoopClientProxy.Instance;
        public IClientProxy Client(string connectionId) => NoopClientProxy.Instance;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => NoopClientProxy.Instance;
        public IClientProxy Group(string groupName) => NoopClientProxy.Instance;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => NoopClientProxy.Instance;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => NoopClientProxy.Instance;
        public IClientProxy User(string userId) => NoopClientProxy.Instance;
        public IClientProxy Users(IReadOnlyList<string> userIds) => NoopClientProxy.Instance;
    }

    private sealed class NoopClientProxy : IClientProxy
    {
        public static readonly NoopClientProxy Instance = new();
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopGroupManager : IGroupManager
    {
        public static readonly NoopGroupManager Instance = new();
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
