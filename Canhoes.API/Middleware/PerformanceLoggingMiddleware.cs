using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Canhoes.Api.Middleware;

/// <summary>
/// Middleware that logs every request with method, path, status code, and duration in milliseconds.
/// Slow requests (>100ms) are logged as WARNING, very slow (>500ms) as ERROR.
/// </summary>
public sealed class PerformanceLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceLoggingMiddleware> _logger;

    // Thresholds for slow request logging
    private const int SlowThresholdMs = 100;
    private const int VerySlowThresholdMs = 500;

    public PerformanceLoggingMiddleware(RequestDelegate next, ILogger<PerformanceLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var sw = Stopwatch.StartNew();

        // Capture the request info
        var method = context.Request.Method;
        var path = context.Request.Path.Value;

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var elapsedMs = sw.ElapsedMilliseconds;
            var statusCode = context.Response.StatusCode;

            // Skip logging for health checks and static files to reduce noise
            var isExcluded = path != null && (path.StartsWith("/health") || path.StartsWith("/swagger"));

            if (!isExcluded)
            {
                if (elapsedMs >= VerySlowThresholdMs)
                {
                    _logger.LogError(
                        "[PERF] VERY SLOW REQUEST: {Method} {Path} => {StatusCode} in {ElapsedMs}ms",
                        method, path, statusCode, elapsedMs);
                }
                else if (elapsedMs >= SlowThresholdMs)
                {
                    _logger.LogWarning(
                        "[PERF] SLOW REQUEST: {Method} {Path} => {StatusCode} in {ElapsedMs}ms",
                        method, path, statusCode, elapsedMs);
                }
                else
                {
                    _logger.LogDebug(
                        "[PERF] {Method} {Path} => {StatusCode} in {ElapsedMs}ms",
                        method, path, statusCode, elapsedMs);
                }
            }
        }
    }
}
