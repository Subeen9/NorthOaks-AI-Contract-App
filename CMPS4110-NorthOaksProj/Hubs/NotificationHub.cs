using Microsoft.AspNetCore.SignalR;

namespace CMPS4110_NorthOaksProj.Hubs
{
    public class NotificationHub : Hub
    {
        // Called by the client after connection established
        public async Task JoinUserGroup(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                Console.WriteLine($" Invalid JoinUserGroup call — userId missing (ConnId: {Context.ConnectionId})");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
            Console.WriteLine($" Connection {Context.ConnectionId} joined group {userId}");
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
           
            Console.WriteLine($" Connection {Context.ConnectionId} disconnected from NotificationHub");
            await base.OnDisconnectedAsync(exception);
        }
    }
}
