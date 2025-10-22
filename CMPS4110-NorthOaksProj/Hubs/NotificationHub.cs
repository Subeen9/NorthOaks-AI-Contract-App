using Microsoft.AspNetCore.SignalR;

namespace CMPS4110_NorthOaksProj.Hubs
{
    public class NotificationHub : Hub
    {
        public async Task JoinUserGroup(string userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }
    }
}
