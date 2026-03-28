using Microsoft.EntityFrameworkCore;
using Canhoes.Api.Data;
using Canhoes.Api.Models;

namespace Canhoes.Api.Startup;

internal static class EventContextBootstrap
{
    public static void EnsureDefaultEventContext(CanhoesDbContext db)
    {
        EnsureEventTables(db);

        var hasChanges = false;

        var defaultEvent = db.Events.FirstOrDefault(x => x.Id == EventContextDefaults.DefaultEventId);
        if (defaultEvent is null)
        {
            db.Events.Add(new EventEntity
            {
                Id = EventContextDefaults.DefaultEventId,
                Name = EventContextDefaults.DefaultEventName,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            hasChanges = true;
        }

        var users = db.Users.ToList();
        var members = db.EventMembers
            .Where(x => x.EventId == EventContextDefaults.DefaultEventId)
            .ToList();

        foreach (var user in users)
        {
            var role = user.IsAdmin ? EventRoles.Admin : EventRoles.User;
            var member = members.FirstOrDefault(x => x.UserId == user.Id);

            if (member is null)
            {
                db.EventMembers.Add(new EventMemberEntity
                {
                    EventId = EventContextDefaults.DefaultEventId,
                    UserId = user.Id,
                    Role = role,
                    JoinedAtUtc = user.CreatedAt == default ? DateTime.UtcNow : user.CreatedAt
                });
                hasChanges = true;
                continue;
            }

            if (member.Role != role)
            {
                member.Role = role;
                hasChanges = true;
            }
        }

        var phases = db.EventPhases
            .Where(x => x.EventId == EventContextDefaults.DefaultEventId)
            .ToList();

        if (phases.Count == 0)
        {
            foreach (var phase in BuildDefaultPhases())
            {
                db.EventPhases.Add(phase);
            }

            hasChanges = true;
        }

        if (hasChanges)
        {
            db.SaveChanges();
        }

        var legacyState = db.CanhoesEventState.FirstOrDefault();
        if (legacyState is not null)
        {
            SyncLegacyPhaseState(db, legacyState.Phase, legacyState.ResultsVisible);
        }
    }

    public static void SyncLegacyPhaseState(CanhoesDbContext db, string legacyPhase, bool resultsVisible)
    {
        EnsureEventTables(db);

        var phases = db.EventPhases
            .Where(x => x.EventId == EventContextDefaults.DefaultEventId)
            .ToList();

        if (phases.Count == 0)
        {
            EnsureDefaultEventContext(db);
            phases = db.EventPhases
                .Where(x => x.EventId == EventContextDefaults.DefaultEventId)
                .ToList();
        }

        var activeType = MapLegacyPhaseType(legacyPhase, resultsVisible);
        var hasChanges = false;

        foreach (var phase in phases)
        {
            var shouldBeActive = phase.Type == activeType;
            if (phase.IsActive == shouldBeActive) continue;

            phase.IsActive = shouldBeActive;
            hasChanges = true;
        }

        if (hasChanges)
        {
            db.SaveChanges();
        }
    }

    private static List<EventPhaseEntity> BuildDefaultPhases()
    {
        var year = DateTime.UtcNow.Year;
        return
        [
            new EventPhaseEntity
            {
                EventId = EventContextDefaults.DefaultEventId,
                Type = EventPhaseTypes.Draw,
                StartDateUtc = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDateUtc = new DateTime(year, 1, 31, 23, 59, 59, DateTimeKind.Utc),
                IsActive = false
            },
            new EventPhaseEntity
            {
                EventId = EventContextDefaults.DefaultEventId,
                Type = EventPhaseTypes.Proposals,
                StartDateUtc = new DateTime(year, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDateUtc = new DateTime(year, 7, 31, 23, 59, 59, DateTimeKind.Utc),
                IsActive = true
            },
            new EventPhaseEntity
            {
                EventId = EventContextDefaults.DefaultEventId,
                Type = EventPhaseTypes.Voting,
                StartDateUtc = new DateTime(year, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDateUtc = new DateTime(year, 11, 30, 23, 59, 59, DateTimeKind.Utc),
                IsActive = false
            },
            new EventPhaseEntity
            {
                EventId = EventContextDefaults.DefaultEventId,
                Type = EventPhaseTypes.Results,
                StartDateUtc = new DateTime(year, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDateUtc = new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                IsActive = false
            }
        ];
    }

    private static string MapLegacyPhaseType(string legacyPhase, bool resultsVisible)
    {
        return legacyPhase.Trim().ToLowerInvariant() switch
        {
            "nominations" => EventPhaseTypes.Proposals,
            "voting" => EventPhaseTypes.Voting,
            "gala" => EventPhaseTypes.Results,
            "locked" when resultsVisible => EventPhaseTypes.Results,
            "locked" => EventPhaseTypes.Draw,
            _ => EventPhaseTypes.Draw
        };
    }

    private static void EnsureEventTables(CanhoesDbContext db)
    {
        if (!db.Database.IsRelational()) return;

        var provider = db.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)) return;

        db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID('dbo.Events', 'U') IS NULL
BEGIN
  CREATE TABLE Events (
    Id NVARCHAR(64) NOT NULL PRIMARY KEY,
    Name NVARCHAR(128) NOT NULL,
    IsActive BIT NOT NULL DEFAULT(1),
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
END

IF OBJECT_ID('dbo.EventMembers', 'U') IS NULL
BEGIN
  CREATE TABLE EventMembers (
    Id NVARCHAR(64) NOT NULL PRIMARY KEY,
    EventId NVARCHAR(64) NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    Role NVARCHAR(16) NOT NULL,
    JoinedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
END

IF OBJECT_ID('dbo.EventPhases', 'U') IS NULL
BEGIN
  CREATE TABLE EventPhases (
    Id NVARCHAR(64) NOT NULL PRIMARY KEY,
    EventId NVARCHAR(64) NOT NULL,
    Type NVARCHAR(32) NOT NULL,
    StartDateUtc DATETIME2 NOT NULL,
    EndDateUtc DATETIME2 NOT NULL,
    IsActive BIT NOT NULL DEFAULT(0)
  );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Events_IsActive' AND object_id = OBJECT_ID('dbo.Events'))
  CREATE INDEX IX_Events_IsActive ON Events(IsActive);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventMembers_EventId_UserId' AND object_id = OBJECT_ID('dbo.EventMembers'))
  CREATE UNIQUE INDEX IX_EventMembers_EventId_UserId ON EventMembers(EventId, UserId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventPhases_EventId_Type' AND object_id = OBJECT_ID('dbo.EventPhases'))
  CREATE UNIQUE INDEX IX_EventPhases_EventId_Type ON EventPhases(EventId, Type);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventPhases_EventId_IsActive' AND object_id = OBJECT_ID('dbo.EventPhases'))
  CREATE INDEX IX_EventPhases_EventId_IsActive ON EventPhases(EventId, IsActive);

IF OBJECT_ID('dbo.AwardCategories', 'U') IS NOT NULL AND COL_LENGTH('dbo.AwardCategories', 'EventId') IS NULL
BEGIN
  ALTER TABLE dbo.AwardCategories ADD EventId NVARCHAR(64) NULL;
  UPDATE dbo.AwardCategories SET EventId = 'canhoes-do-ano' WHERE EventId IS NULL;
  ALTER TABLE dbo.AwardCategories ALTER COLUMN EventId NVARCHAR(64) NOT NULL;
END

IF OBJECT_ID('dbo.Nominees', 'U') IS NOT NULL AND COL_LENGTH('dbo.Nominees', 'EventId') IS NULL
BEGIN
  ALTER TABLE dbo.Nominees ADD EventId NVARCHAR(64) NULL;
  UPDATE dbo.Nominees SET EventId = 'canhoes-do-ano' WHERE EventId IS NULL;
  ALTER TABLE dbo.Nominees ALTER COLUMN EventId NVARCHAR(64) NOT NULL;
END

IF OBJECT_ID('dbo.CategoryProposals', 'U') IS NOT NULL AND COL_LENGTH('dbo.CategoryProposals', 'EventId') IS NULL
BEGIN
  ALTER TABLE dbo.CategoryProposals ADD EventId NVARCHAR(64) NULL;
  UPDATE dbo.CategoryProposals SET EventId = 'canhoes-do-ano' WHERE EventId IS NULL;
  ALTER TABLE dbo.CategoryProposals ALTER COLUMN EventId NVARCHAR(64) NOT NULL;
END

IF OBJECT_ID('dbo.MeasureProposals', 'U') IS NOT NULL AND COL_LENGTH('dbo.MeasureProposals', 'EventId') IS NULL
BEGIN
  ALTER TABLE dbo.MeasureProposals ADD EventId NVARCHAR(64) NULL;
  UPDATE dbo.MeasureProposals SET EventId = 'canhoes-do-ano' WHERE EventId IS NULL;
  ALTER TABLE dbo.MeasureProposals ALTER COLUMN EventId NVARCHAR(64) NOT NULL;
END

IF OBJECT_ID('dbo.Measures', 'U') IS NOT NULL AND COL_LENGTH('dbo.Measures', 'EventId') IS NULL
BEGIN
  ALTER TABLE dbo.Measures ADD EventId NVARCHAR(64) NULL;
  UPDATE dbo.Measures SET EventId = 'canhoes-do-ano' WHERE EventId IS NULL;
  ALTER TABLE dbo.Measures ALTER COLUMN EventId NVARCHAR(64) NOT NULL;
END

IF OBJECT_ID('dbo.WishlistItems', 'U') IS NOT NULL AND COL_LENGTH('dbo.WishlistItems', 'EventId') IS NULL
BEGIN
  ALTER TABLE dbo.WishlistItems ADD EventId NVARCHAR(64) NULL;
  UPDATE dbo.WishlistItems SET EventId = 'canhoes-do-ano' WHERE EventId IS NULL;
  ALTER TABLE dbo.WishlistItems ALTER COLUMN EventId NVARCHAR(64) NOT NULL;
END

IF OBJECT_ID('dbo.HubPosts', 'U') IS NOT NULL AND COL_LENGTH('dbo.HubPosts', 'EventId') IS NULL
BEGIN
  ALTER TABLE dbo.HubPosts ADD EventId NVARCHAR(64) NULL;
  UPDATE dbo.HubPosts SET EventId = 'canhoes-do-ano' WHERE EventId IS NULL;
  ALTER TABLE dbo.HubPosts ALTER COLUMN EventId NVARCHAR(64) NOT NULL;
END

IF OBJECT_ID('dbo.HubPosts', 'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HubPosts_EventId' AND object_id = OBJECT_ID('dbo.HubPosts'))
BEGIN
  CREATE INDEX IX_HubPosts_EventId ON dbo.HubPosts(EventId);
END
");
    }
}
