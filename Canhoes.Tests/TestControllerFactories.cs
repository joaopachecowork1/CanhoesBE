using Canhoes.Api.Controllers;
using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

namespace Canhoes.Tests;

internal static class TestControllerFactories
{
    public static CanhoesController CreateCanhoesController(CanhoesDbContext db, Guid userId, bool isAdmin = false)
    {
        var controller = new CanhoesController(db, new FakeWebHostEnvironment(), new MemoryCache(new MemoryCacheOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ControllerContext.HttpContext.Items["UserId"] = userId;
        controller.ControllerContext.HttpContext.Items["IsAdmin"] = isAdmin;
        return controller;
    }

    public static EventsController CreateEventsController(CanhoesDbContext db, Guid userId, bool isAdmin = false)
    {
        var controller = new EventsController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.ControllerContext.HttpContext.Items["UserId"] = userId;
        controller.ControllerContext.HttpContext.Items["IsAdmin"] = isAdmin;
        return controller;
    }

    public static HubController CreateHubController(CanhoesDbContext db, Guid userId, bool isAdmin = false)
    {
        var controller = new HubController(db, new FakeWebHostEnvironment())
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
