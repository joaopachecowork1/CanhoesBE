using Canhoes.Api.DTOs;
using Canhoes.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Canhoes.Api.Services;

public interface IEventService
{
    Task<List<EventSummaryDto>> GetEventSummariesAsync(CancellationToken ct);
    Task<EventContextDto?> GetEventContextAsync(string eventId, Guid userId, bool isAdmin, CancellationToken ct);
    Task<EventOverviewDto?> GetEventOverviewAsync(string eventId, Guid userId, bool isAdmin, CancellationToken ct);
    Task<SecretSantaDrawDto?> GetSecretSantaDrawAsync(string eventId, Guid userId, CancellationToken ct);
    Task<EventActiveContextDto?> GetActiveEventContextAsync(Guid userId, bool isAdmin, CancellationToken ct);
    Task<EventHomeSnapshotDto?> GetActiveHomeSnapshotAsync(Guid userId, bool isAdmin, CancellationToken ct);
}
