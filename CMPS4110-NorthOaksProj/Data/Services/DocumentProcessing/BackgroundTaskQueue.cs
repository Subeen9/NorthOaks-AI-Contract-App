using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing
{
    public interface IBackgroundTaskQueue
    {
        void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem);
        Task ProcessQueueAsync(CancellationToken token);
        void QueueContractProcessing(int contractId, string rootPath, string userGroup, IHubContext<ProcessingHub> hubContext);

    }

    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<Func<CancellationToken, Task>> _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BackgroundTaskQueue> _logger;

        public BackgroundTaskQueue(IServiceScopeFactory scopeFactory, ILogger<BackgroundTaskQueue> logger)
        {
            _queue = Channel.CreateUnbounded<Func<CancellationToken, Task>>();
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
            => _queue.Writer.TryWrite(workItem);

        public async Task ProcessQueueAsync(CancellationToken token)
        {
            await foreach (var workItem in _queue.Reader.ReadAllAsync(token))
            {
                try { await workItem(token); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing background work item.");
                }
            }
        }

        public void QueueContractProcessing(int contractId, string rootPath, string userGroup, IHubContext<ProcessingHub> hubContext)
        {
            QueueBackgroundWorkItem(async token =>
            {
                using var scope = _scopeFactory.CreateScope();
                var contractsService = scope.ServiceProvider.GetRequiredService<IContractsService>();

                try
                {
                    await contractsService.ProcessContractAsync(contractId, rootPath, token);
                    await hubContext.Clients.Group(userGroup).SendAsync("ProcessingUpdate", new
                    {
                        message = "Processing complete!",
                        progress = 100
                    });
                }
                catch (Exception ex)
                {
                    await hubContext.Clients.Group(userGroup).SendAsync("ProcessingUpdate", new
                    {
                        message = $"Error processing file: {ex.Message}",
                        progress = -1
                    });
                }
            });
        }
    }


}
