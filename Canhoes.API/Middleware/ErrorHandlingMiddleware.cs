using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Canhoes.Api.Middleware;

/// <summary>
/// Global exception handler that captures all unhandled exceptions and returns
/// a standardized RFC 7807 Problem Details response.
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger, IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FATAL UNHANDLED EXCEPTION: {Method} {Path}", context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "Unhandled exception processing request {Method} {Path}.", context.Request.Method, context.Request.Path);

        var statusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = "An unhandled error occurred while processing your request.",
            Detail = _env.IsDevelopment() ? exception.Message : "Please contact support if the issue persists.",
            Instance = context.Request.Path,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1"
        };

        // Add custom extension for TraceId to aid debugging
        problem.Extensions["traceId"] = context.TraceIdentifier;

        if (_env.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.ToString();
        }

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, options));
    }
}
