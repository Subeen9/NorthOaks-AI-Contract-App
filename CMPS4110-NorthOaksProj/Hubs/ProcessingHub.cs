using Microsoft.AspNetCore.SignalR;

namespace CMPS4110_NorthOaksProj.Hubs
{
    public class ProcessingHub : Hub
    {
        public async Task JoinProcessingGroup(string userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }
        public async Task LeaveProcessingGroup(string userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
        }
    }
}
