using Canhoes.Api.Models;
using Canhoes.Api.Repositories;
using Canhoes.Api.DTOs;
using static Canhoes.Api.Mappers.EventMappers;

namespace Canhoes.Api.Services;

/// <summary>
/// Centralizes Secret Santa draw logic.
/// </summary>
public sealed class SecretSantaService : ISecretSantaService
{
    private readonly ISecretSantaRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IEventRepository _eventRepository;

    public SecretSantaService(
        ISecretSantaRepository repository,
        IUserRepository userRepository,
        IEventRepository eventRepository)
    {
        _repository = repository;
        _userRepository = userRepository;
        _eventRepository = eventRepository;
    }

    public async Task<EventSecretSantaOverviewDto?> GetOverviewAsync(string eventId, Guid userId, CancellationToken ct)
    {
        var draw = await _repository.GetLatestDrawAsync(eventId, ct);
        if (draw is null)
        {
            return new EventSecretSantaOverviewDto(eventId, false, false, null, null, 0, 0);
        }

        var assignment = await _repository.GetAssignmentAsync(draw.Id, userId, ct);
        EventUserDto? assignedUserDto = null;
        int assignedWishlistItemCount = 0;

        if (assignment != null)
        {
            var targetUser = await _userRepository.GetUserAsync(assignment.ReceiverUserId, ct);
            if (targetUser != null)
            {
                assignedUserDto = new EventUserDto(targetUser.Id, targetUser.DisplayName ?? targetUser.Email, "user");
                // Get wishlist count for target user
                // For now, simplified
            }
        }

        var myWishlistCount = await _eventRepository.CountWishlistItemsAsync(eventId, userId, ct);

        return new EventSecretSantaOverviewDto(
            eventId,
            true,
            assignment != null,
            draw.EventCode,
            assignedUserDto,
            assignedWishlistItemCount,
            myWishlistCount
        );
    }

    public async Task<SecretSantaDrawResult> ExecuteDrawAsync(
        string eventCode,
        IReadOnlyList<UserEntity> participants,
        Guid createdByUserId,
        CancellationToken ct)
    {
        if (participants.Count < 2)
        {
            return SecretSantaDrawResult.Failed("Need at least 2 members to draw.");
        }

        var existingDraws = await _repository.GetDrawsByEventCodeAsync(eventCode, ct);

        if (existingDraws.Count > 0)
        {
            var drawIds = existingDraws.Select(d => d.Id).ToList();
            var existingAssignments = await _repository.GetAssignmentsByDrawIdsAsync(drawIds, ct);
            _repository.RemoveAssignments(existingAssignments);
            _repository.RemoveDraws(existingDraws);
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

            await _repository.AddDrawAsync(draw, ct);

            var assignments = new List<SecretSantaAssignmentEntity>(participants.Count);
            for (var i = 0; i < participants.Count; i++)
            {
                assignments.Add(new SecretSantaAssignmentEntity
                {
                    Id = Guid.NewGuid().ToString(),
                    DrawId = draw.Id,
                    GiverUserId = participants[i].Id,
                    ReceiverUserId = shuffled[i].Id,
                });
            }

            await _repository.AddAssignmentsRangeAsync(assignments, ct);
            await _repository.SaveChangesAsync(ct);
            return SecretSantaDrawResult.Success(draw);
        }

        return SecretSantaDrawResult.Failed($"Could not create a valid draw after {maxAttempts} attempts. Please try again.");
    }
}
