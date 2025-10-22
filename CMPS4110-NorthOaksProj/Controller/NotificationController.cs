using CMPS4110_NorthOaksProj.Data.Services.Notifications;
using CMPS4110_NorthOaksProj.Models.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace CMPS4110_NorthOaksProj.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationsController : ControllerBase
    {
        private readonly NotificationService _service;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(NotificationService service, ILogger<NotificationsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        // ============================
        // GET UNREAD NOTIFICATIONS
        // ============================
        [HttpGet("unread/{userId:int}")]
        public async Task<ActionResult<IEnumerable<Notification>>> GetUnread(int userId)
        {
            try
            {
                var items = await _service.GetUnreadAsync(userId);
                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching unread notifications for user {UserId}", userId);
                return StatusCode(500, "An error occurred while fetching notifications.");
            }
        }

        // ============================
        // MARK ALL AS READ
        // ============================
        [HttpPost("mark-read/{userId:int}")]
        public async Task<IActionResult> MarkRead(int userId)
        {
            try
            {
                await _service.MarkAllAsReadAsync(userId);
                return Ok(new { message = "Notifications marked as read." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notifications as read for user {UserId}", userId);
                return StatusCode(500, "An error occurred while marking notifications as read.");
            }
        }
    }
}
