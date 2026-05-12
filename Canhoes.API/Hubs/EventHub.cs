using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Canhoes.Api.Hubs;

[Authorize]
public sealed class EventHub : Hub
{
    public async Task JoinEventGroup(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 64)
            throw new HubException("Invalid eventId.");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"event_{eventId}");
    }

    public async Task LeaveEventGroup(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 64)
            throw new HubException("Invalid eventId.");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"event_{eventId}");
    }
}
