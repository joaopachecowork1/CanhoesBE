using Canhoes.Api.Controllers;
using Canhoes.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Canhoes.Tests;

internal static class ControllerHelpers
{
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
}
