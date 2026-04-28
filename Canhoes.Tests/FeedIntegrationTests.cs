using System.Net.Http.Json;
using Canhoes.Api.Data;
using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canhoes.Tests;

public class FeedIntegrationTests : IClassFixture<CanhoesWebApplicationFactory>
{
    private readonly CanhoesWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FeedIntegrationTests(CanhoesWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreatePost_Should_SaveToDatabaseAndReturnDto()
    {
        // Arrange
        var eventId = "test-event-" + Guid.NewGuid().ToString("N")[..8];
        var userEmail = "test@canhoes.com";
        
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CanhoesDbContext>();
            
            // Seed user (mapped from mock auth email)
            var user = new UserEntity 
            { 
                Id = Guid.NewGuid(), 
                ExternalId = userEmail, 
                Email = userEmail, 
                DisplayName = "Test User",
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            
            // Seed event
            var @event = new EventEntity { Id = eventId, Name = "Test Event", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
            db.Events.Add(@event);
            
            // Seed member
            db.EventMembers.Add(new EventMemberEntity 
            { 
                Id = Guid.NewGuid().ToString(), 
                EventId = eventId, 
                UserId = user.Id, 
                Role = "user", 
                JoinedAtUtc = DateTime.UtcNow 
            });

            // Seed phases to enable Feed module
            db.EventPhases.Add(new EventPhaseEntity 
            { 
                Id = eventId + "-feed", 
                EventId = eventId, 
                Type = "feed", 
                StartDateUtc = DateTime.UtcNow.AddDays(-1), 
                EndDateUtc = DateTime.UtcNow.AddDays(1), 
                IsActive = true 
            });

            await db.SaveChangesAsync();
        }

        var request = new CreateEventPostRequest("Hello from integration test!", null);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/events/{eventId}/feed/posts", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EventFeedPostDto>();
        result.Should().NotBeNull();
        result!.Content.Should().Be(request.Content);

        // Verify in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CanhoesDbContext>();
            var post = await db.HubPosts.FirstOrDefaultAsync(p => p.Id == result.Id);
            post.Should().NotBeNull();
            post!.Text.Should().Be(request.Content);
            post.EventId.Should().Be(eventId);
        }
    }
}
