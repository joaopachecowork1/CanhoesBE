using Canhoes.Api.Data;
using Canhoes.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Canhoes.Api.Services;

/// <summary>
/// Centralizes Secret Santa draw logic to avoid duplication between
/// EventsController and CanhoesController.
/// </summary>
public sealed class SecretSantaService
{
    public async Task<SecretSantaDrawResult> ExecuteDrawAsync(
        CanhoesDbContext db,
        string eventCode,
        IReadOnlyList<UserEntity> participants,
        Guid createdByUserId,
        CancellationToken ct)
    {
        if (participants.Count < 2)
        {
            return SecretSantaDrawResult.Failed("Need at least 2 members to draw.");
        }

        var existingDraws = await db.SecretSantaDraws
            .Where(x => x.EventCode == eventCode)
            .ToListAsync(ct);

        if (existingDraws.Count > 0)
        {
            foreach (var existingDraw in existingDraws)
            {
                var existingAssignments = db.SecretSantaAssignments.Where(x => x.DrawId == existingDraw.Id);
                db.SecretSantaAssignments.RemoveRange(existingAssignments);
            }

            db.SecretSantaDraws.RemoveRange(existingDraws);
        }

        const int maxAttempts = 100;
        var rng = Random.Shared;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var shuffled = participants.ToList();
            for (var i = shuffled.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            var isValid = true;
            for (var i = 0; i < participants.Count; i++)
            {
                if (participants[i].Id == shuffled[i].Id)
                {
                    isValid = false;
                    break;
                }
            }

            if (!isValid) continue;

            var draw = new SecretSantaDrawEntity
            {
                Id = Guid.NewGuid().ToString(),
                EventCode = eventCode,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = createdByUserId,
                IsLocked = true,
            };

            db.SecretSantaDraws.Add(draw);

            for (var i = 0; i < participants.Count; i++)
            {
                db.SecretSantaAssignments.Add(new SecretSantaAssignmentEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    DrawId = draw.Id,
                    GiverUserId = participants[i].Id,
                    ReceiverUserId = shuffled[i].Id,
                });
            }

            await db.SaveChangesAsync(ct);
            return SecretSantaDrawResult.Success(draw);
        }

        return SecretSantaDrawResult.Failed($"Could not create a valid draw after {maxAttempts} attempts. Please try again.");
    }
}

public sealed record SecretSantaDrawResult(
    bool IsSuccess,
    SecretSantaDrawEntity? Draw,
    string? ErrorMessage)
{
    public static SecretSantaDrawResult Success(SecretSantaDrawEntity draw) =>
        new(true, draw, null);

    public static SecretSantaDrawResult Failed(string error) =>
        new(false, null, error);
}
