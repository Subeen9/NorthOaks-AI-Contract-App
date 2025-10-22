using CMPS4110_NorthOaksProj.Models.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CMPS4110_NorthOaksProj.Data.Services.Notifications
{
    public class NotificationService
    {
        private readonly DataContext _context;

        public NotificationService(DataContext context)
        {
            _context = context;
        }

        public async Task SaveNotificationAsync(string message, string triggeredByUserId)
        {
            // convert JWT string user ID to int
            if (!int.TryParse(triggeredByUserId, out int triggeredUserId))
                throw new InvalidOperationException($"Invalid user ID format: {triggeredByUserId}");

            // get all other users except the one who triggered it
            var targetIds = await _context.Users
                .Where(u => u.Id != triggeredUserId)
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var id in targetIds)
            {
                _context.Notifications.Add(new Notification
                {
                    Message = message,
                    TargetUserId = id,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }

        public async Task<List<Notification>> GetUnreadAsync(int userId)
        {
            return await _context.Notifications
                .Where(n => n.TargetUserId == userId && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
        }

        public async Task MarkAllAsReadAsync(int userId)
        {
            var items = await _context.Notifications
                .Where(n => n.TargetUserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var n in items)
                n.IsRead = true;

            await _context.SaveChangesAsync();
        }
    }
}
