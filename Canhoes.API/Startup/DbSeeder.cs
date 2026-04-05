using Microsoft.EntityFrameworkCore;
using Canhoes.Api.Data;
using Canhoes.Api.Models;

namespace Canhoes.Api.Startup;

/// <summary>
/// Seeds the database with the minimum required data on first startup.
/// All operations are idempotent – safe to run on every app start.
/// </summary>
internal static class DbSeeder
{
    public static void Seed(CanhoesDbContext db, string? webRootPath = null)
    {
        EnsureMockUser(db);
        EnsureCanhoesEventState(db);
        EnsureDefaultAwardCategories(db);
        EventContextBootstrap.EnsureDefaultEventContext(db);
        EnsureUploadDirectories(webRootPath);
    }

    private static void EnsureMockUser(CanhoesDbContext db)
    {
        var mockUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var mockEmail = "dev@canhoes.com";

        if (db.Users.Any(x => x.Id == mockUserId || x.Email == mockEmail || x.ExternalId == mockEmail))
        {
            return;
        }

        db.Users.Add(new UserEntity
        {
            Id = mockUserId,
            ExternalId = mockEmail,
            Email = mockEmail,
            DisplayName = "Mock Admin",
            IsAdmin = true,
            CreatedAt = DateTime.UtcNow
        });

        db.SaveChanges();
    }

    private static void EnsureCanhoesEventState(CanhoesDbContext db)
    {
        if (db.CanhoesEventState.Any(x => x.EventId == EventContextDefaults.DefaultEventId)) return;

        var nextId = (db.CanhoesEventState.Max(x => (int?)x.Id) ?? 0) + 1;
        var state = new CanhoesEventStateEntity
        {
            Id = nextId,
            EventId = EventContextDefaults.DefaultEventId,
            Phase = "nominations",
            NominationsVisible = true,
            ResultsVisible = false,
            ModuleVisibilityJson = "{}"
        };

        db.CanhoesEventState.Add(state);
        db.SaveChanges();
    }

    private static void EnsureDefaultAwardCategories(CanhoesDbContext db)
    {
        if (db.AwardCategories.Any()) return;

        db.AwardCategories.AddRange(
            new AwardCategoryEntity { Name = "Sticker do Ano", SortOrder = 1, Kind = AwardCategoryKind.Sticker },
            new AwardCategoryEntity { Name = "Melhor Ano", SortOrder = 10, Kind = AwardCategoryKind.UserVote },
            new AwardCategoryEntity { Name = "Alterna do Ano", SortOrder = 11, Kind = AwardCategoryKind.UserVote },
            new AwardCategoryEntity { Name = "Aterrado do Ano", SortOrder = 12, Kind = AwardCategoryKind.UserVote }
        );
        db.SaveChanges();
    }

    private static void EnsureUploadDirectories(string? webRootPath)
    {
        var webRoot = string.IsNullOrWhiteSpace(webRootPath) ? "wwwroot" : webRootPath;
        Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "canhoes"));
        Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "hub"));
    }
}
