using Microsoft.AspNetCore.SignalR;

namespace CMPS4110_NorthOaksProj.Hubs
{
    public class ProcessingHub : Hub
    {
        public async Task JoinUserGroup(string userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }

        public async Task JoinContractProcessing(int contractId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"contract-{contractId}");
            Console.WriteLine($"Connection {Context.ConnectionId} joined contract-{contractId} processing group");
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Console.WriteLine($"Connection {Context.ConnectionId} disconnected from ProcessingHub");
            await base.OnDisconnectedAsync(exception);
        }
    }
}
