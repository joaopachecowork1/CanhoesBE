using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Canhoes.Api.Middleware;

/// <summary>
/// Tracks request performance metrics in memory.
/// Registered as a singleton and updated by the PerformanceMetricsMiddleware.
/// </summary>
public sealed class RequestMetricsCollector
{
    // Keep last 100 request durations per endpoint (method + path)
    private readonly ConcurrentDictionary<string, ConcurrentQueue<int>> _endpointDurations = new();
    private readonly ConcurrentQueue<(string Method, string Path, int DurationMs)> _recentRequests = new();
    private int _totalRequests;
    private long _totalDurationMs;

    public void Record(string method, string path, int durationMs)
    {
        _totalRequests++;
        _totalDurationMs += durationMs;

        // Keep only last 100 requests for the summary
        _recentRequests.Enqueue((method, path, durationMs));
        while (_recentRequests.Count > 100)
            _recentRequests.TryDequeue(out _);

        // Track per-endpoint metrics
        var key = $"{method} {path}";
        var queue = _endpointDurations.GetOrAdd(key, _ => new ConcurrentQueue<int>());
        queue.Enqueue(durationMs);
        while (queue.Count > 100)
            queue.TryDequeue(out _);
    }

    public PerfMetricsDto GetMetrics()
    {
        var recent = _recentRequests.ToArray();
        var avgMs = recent.Length > 0 ? Math.Round(recent.Average(r => r.DurationMs)) : 0;
        var slowest = recent.OrderByDescending(r => r.DurationMs).Take(5).ToArray();

        // Compute per-endpoint averages
        var endpointAvgs = _endpointDurations
            .Select(kvp => new EndpointAvgDto(
                kvp.Key,
                kvp.Value.Count,
                kvp.Value.Count > 0 ? Math.Round(kvp.Value.Average()) : 0))
            .OrderByDescending(e => e.AverageMs)
            .Take(10)
            .ToList();

        return new PerfMetricsDto(
            TotalRequests: _totalRequests,
            RecentRequests: recent.Length,
            AverageResponseTimeMs: avgMs,
            SlowestRequests: slowest.Select(r => new SlowRequestDto(r.Method, r.Path, r.DurationMs)).ToList(),
            TopEndpoints: endpointAvgs);
    }
}

public record PerfMetricsDto(
    int TotalRequests,
    int RecentRequests,
    double AverageResponseTimeMs,
    List<SlowRequestDto> SlowestRequests,
    List<EndpointAvgDto> TopEndpoints);

public record SlowRequestDto(string Method, string Path, int DurationMs);

public record EndpointAvgDto(string Endpoint, int SampleCount, double AverageMs);

/// <summary>
/// Middleware that records request durations into the RequestMetricsCollector.
/// </summary>
public sealed class PerformanceMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestMetricsCollector _metrics;

    public PerformanceMetricsMiddleware(RequestDelegate next, RequestMetricsCollector metrics)
    {
        _next = next;
        _metrics = metrics;
    }

    public async Task Invoke(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var path = context.Request.Path.Value ?? "";

            // Skip internal endpoints
            if (!path.StartsWith("/health") && !path.StartsWith("/swagger") && !path.StartsWith("/admin/perf"))
            {
                _metrics.Record(context.Request.Method, path, (int)sw.ElapsedMilliseconds);
            }
        }
    }
}

/// <summary>
/// Admin-only endpoint to view performance metrics.
/// </summary>
public static class PerformanceMetricsEndpoint
{
    public static void MapPerformanceMetrics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/perf", ([FromServices] RequestMetricsCollector metrics) =>
        {
            return Results.Ok(metrics.GetMetrics());
        })
        .RequireAuthorization(); // Requires authenticated user
    }
}
