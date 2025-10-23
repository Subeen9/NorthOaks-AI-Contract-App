namespace NorthOaks.Shared.Model.Notifications
{
    public class NotificationDto
    {
        public int Id { get; set; }
        public string Message { get; set; } = string.Empty;
        public int TargetUserId { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }

        //  for UI convenience — returns user-friendly time label
        public string GetTimeAgo()
        {
            var diff = DateTime.UtcNow - CreatedAt;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} minutes ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} hours ago";
            return CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt");
        }
    }
}
