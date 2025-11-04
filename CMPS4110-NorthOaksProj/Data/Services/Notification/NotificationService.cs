using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CMPS4110_NorthOaksProj.Data.Services.Notifications
{
    public class NotificationService
    {
        private readonly DataContext _context;

        public NotificationService(DataContext context)
        {
            _context = context;
        }

        // Note: This method is no longer used by the controller,

        // The ContractsController has its own save logic.
        public async Task SaveNotificationAsync(string message, int triggeredByUserId)
        {
            var targetIds = await _context.Users
                .Where(u => u.Id != triggeredByUserId)
                .Select(u => u.Id)
                .ToListAsync();

            var newNotifications = new List<Notification>();
            foreach (var userId in targetIds)
            {
                newNotifications.Add(new Notification
                {
                    Message = message,
                    TargetUserId = userId,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.Notifications.AddRangeAsync(newNotifications);
            await _context.SaveChangesAsync();
        }

        // UPDATED: Accepts an int
        public async Task<List<Notification>> GetUnreadAsync(int userId)
        {
            return await _context.Notifications
                .Where(n => n.TargetUserId == userId && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
        }

        // UPDATED: Accepts an int
        public async Task MarkAllAsReadAsync(int userId)
        {
            var items = await _context.Notifications
                .Where(n => n.TargetUserId == userId && !n.IsRead)
                .ToListAsync();

            if (items.Count == 0) return;

            foreach (var n in items)
                n.IsRead = true;

            await _context.SaveChangesAsync();
        }
        public async Task<List<Notification>> GetAllAsync(int userId)
        {
            return await _context.Notifications
                .Where(n => n.TargetUserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

        }

    }

}
    
    