using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CMPS4110_NorthOaksProj.Data;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NorthOaks.Server.Services
{
    public class NotificationCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<NotificationCleanupService> _logger;

        public NotificationCleanupService(IServiceScopeFactory scopeFactory, ILogger<NotificationCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<DataContext>();

                    // Delete notifications that were read more than 6 hours ago
                    var cutoff = DateTime.UtcNow.AddHours(-6);

                    var oldNotifications = db.Notifications
                        .Where(n => n.IsRead && n.CreatedAt < cutoff)
                        .ToList();

                    if (oldNotifications.Any())
                    {
                        db.Notifications.RemoveRange(oldNotifications);
                        await db.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation($"Deleted {oldNotifications.Count} old notifications at {DateTime.UtcNow} UTC.");
                    }
                    else
                    {
                        _logger.LogInformation($" No old notifications found at {DateTime.UtcNow} UTC.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cleaning notifications");
                }

                //  Run every hour
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}
