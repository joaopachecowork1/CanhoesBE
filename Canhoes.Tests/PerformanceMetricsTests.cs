using FluentAssertions;
using Canhoes.Api.Middleware;
using Xunit;

namespace Canhoes.Tests;

public class PerformanceMetricsTests
{
    [Fact]
    public void Collector_ShouldAggregateAndRankSlowRequests()
    {
        var collector = new RequestMetricsCollector();

        collector.Record("GET", "/api/me", 5);
        collector.Record("GET", "/api/me", 7);
        collector.Record("GET", "/api/users", 20);
        collector.Record("POST", "/api/auth/login", 12);

        var metrics = collector.GetMetrics();

        metrics.TotalRequests.Should().Be(4);
        metrics.RecentRequests.Should().Be(4);
        metrics.AverageResponseTimeMs.Should().BeGreaterThan(0);
        metrics.TopEndpoints.Should().NotBeEmpty();
        metrics.TopEndpoints.First().Endpoint.Should().Contain("/api/users");
    }
}
