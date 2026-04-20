using FluentAssertions;
using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Canhoes.Tests;

public sealed class ControllerCoverageTests
{
    [Fact]
    public async Task Wishlist_ShouldReturnPageShape()
    {
        await using var db = TestSupport.CreateDbContext();
        var userId = Guid.NewGuid();
        TestSupport.SeedEvent(db, "event-wishlist", isActive: true);
        TestSupport.SeedState(db, "event-wishlist", EventPhaseTypes.Proposals, TestSupport.BuildVisibility(wishlist: true));
        TestSupport.SeedMember(db, "event-wishlist", userId);
        TestSupport.SeedUser(db, userId, "u@example.com", "User");
        db.WishlistItems.Add(new WishlistItemEntity
        {
            Id = "wish-1",
            EventId = "event-wishlist",
            UserId = userId,
            Title = "Livro",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var controller = TestSupport.CreateCanhoesController(db, userId);
        var result = await controller.GetWishlist(0, 50, CancellationToken.None);
        var page = result.Value.Should().BeOfType<PagedResult<WishlistItemDto>>().Subject;

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle(x => x.Title == "Livro");
    }

    [Fact]
    public async Task SecretSanta_ShouldReturnReceiverShapeOrNotFound()
    {
        await using var db = TestSupport.CreateDbContext();
        var userId = Guid.NewGuid();
        TestSupport.SeedEvent(db, "event-secret", isActive: true);
        TestSupport.SeedState(db, "event-secret", EventPhaseTypes.Proposals, TestSupport.BuildVisibility(secretSanta: true));
        TestSupport.SeedMember(db, "event-secret", userId);
        TestSupport.SeedUser(db, userId, "u@example.com", "User");
        db.SecretSantaDraws.Add(new SecretSantaDrawEntity
        {
            Id = "draw-1",
            EventCode = "event-secret",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId,
            IsLocked = true
        });
        db.SaveChanges();

        var controller = TestSupport.CreateCanhoesController(db, userId);
        var result = await controller.GetMySecretSanta("event-secret", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }
}
