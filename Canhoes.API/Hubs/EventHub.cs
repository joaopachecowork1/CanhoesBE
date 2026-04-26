using Microsoft.AspNetCore.SignalR;

namespace Canhoes.Api.Hubs;

public sealed class EventHub : Hub
{
    public async Task JoinEventGroup(string eventId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"event_{eventId}");
    }

    public async Task LeaveEventGroup(string eventId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"event_{eventId}");
    }
}
