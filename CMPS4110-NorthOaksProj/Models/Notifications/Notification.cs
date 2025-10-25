using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMPS4110_NorthOaksProj.Models.Notifications
{
    public class Notification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Message { get; set; } = string.Empty;

        [Required]
        public int TargetUserId { get; set; } 

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
