using System.Security.Claims;
using Canhoes.Api.Auth;
using Canhoes.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Canhoes.Tests;

public sealed class UserContextMiddlewareTests
{
    [Fact]
    public async Task Invoke_ShouldPromoteAllowlistedUserAndPopulateContext()
    {
        await using var db = TestSupport.CreateDbContext();
        var existingAdminId = Guid.NewGuid();
        db.Users.Add(new UserEntity
        {
            Id = existingAdminId,
            ExternalId = $"ext-{existingAdminId:N}",
            Email = "existing-admin@example.com",
            DisplayName = "Existing Admin",
            IsAdmin = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:AdminEmails:0"] = "new-admin@example.com"
            })
            .Build();

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var nextCalled = false;
        var middleware = new UserContextMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<UserContextMiddleware>.Instance,
            config,
            memoryCache);

        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "google-sub-1"),
            new Claim("email", "new-admin@example.com"),
            new Claim(ClaimTypes.Email, "new-admin@example.com"),
            new Claim("name", "New Admin")
        ], "TestAuth"));

        await middleware.Invoke(context, db);

        nextCalled.Should().BeTrue();
        context.Items["IsAdmin"].Should().Be(true);
        context.Items["UserId"].Should().BeOfType<Guid>();
        context.User.Claims.Should().Contain(c => (c.Type == "role" || c.Type == ClaimTypes.Role) && c.Value == "admin");

        db.Users.Should().ContainSingle(user => user.Email == "new-admin@example.com" && user.IsAdmin);
    }
}
