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
        var controller = new EventsController(
            db,
            env: null,
            secretSanta: new SecretSantaService(),
            cache: new MemoryCache(new MemoryCacheOptions()),
            hub: new NoopHubContext())
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
