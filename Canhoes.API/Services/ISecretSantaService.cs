using Canhoes.Api.Models;
using Canhoes.Api.DTOs;

namespace Canhoes.Api.Services;

public interface ISecretSantaService
{
    Task<EventSecretSantaOverviewDto?> GetOverviewAsync(
        string eventId,
        Guid userId,
        CancellationToken ct);

    Task<SecretSantaDrawResult> ExecuteDrawAsync(
        string eventCode,
        IReadOnlyList<UserEntity> participants,
        Guid createdByUserId,
        CancellationToken ct);
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
