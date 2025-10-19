namespace CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing
{
    public class TaskRunner : BackgroundService
    {
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly ILogger<TaskRunner> _logger;
        public TaskRunner(IBackgroundTaskQueue taskQueue, ILogger<TaskRunner> logger)
        {
            _taskQueue = taskQueue;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Task Runner is starting.");
            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem = await _taskQueue.DequeueAsync(stoppingToken);
                try
                {
                    await workItem(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing task.");
                }
            }
            _logger.LogInformation("Task Runner is stopping.");
        }
    }
}
