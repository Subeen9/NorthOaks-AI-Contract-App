using CMPS4110_NorthOaksProj.Data.Services.Notifications;
using CMPS4110_NorthOaksProj.Models.Notifications;
using CMPS4110_NorthOaksProj.Models.Users; // <-- Make sure this 'using' matches your User model location
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity; // <-- Required for UserManager
using Microsoft.AspNetCore.Mvc;
using NorthOaks.Shared.Model.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace CMPS4110_NorthOaksProj.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // <-- This must be enabled
    public class NotificationsController : ControllerBase
    {
        private readonly NotificationService _service;
        private readonly ILogger<NotificationsController> _logger;
        private readonly UserManager<User> _userManager; // <-- Inject UserManager

        // Updated constructor
        public NotificationsController(NotificationService service,
            ILogger<NotificationsController> logger,
            UserManager<User> userManager) // <-- Add UserManager
        {
            _service = service;
            _logger = logger;
            _userManager = userManager; // <-- Set UserManager
        }

        // ============================
        // GET UNREAD NOTIFICATIONS
        // ============================
        // This route now correctly accepts a string username
        [HttpGet("unread")]
        public async Task<ActionResult<IEnumerable<NotificationDto>>> GetUnread()
        {
            try
            {
                // Pull numeric ID from the JWT claim (your token has "uid")
                var currentUserIdStr = User.FindFirstValue("uid");
                if (!int.TryParse(currentUserIdStr, out var currentUserId))
                    return Unauthorized("Invalid user ID in token.");

                var items = await _service.GetUnreadAsync(currentUserId);

                var dtos = items.Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Message = n.Message,
                    CreatedAt = n.CreatedAt,
                    IsRead = n.IsRead,
                    TargetUserId = n.TargetUserId
                }).ToList();

                return Ok(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching unread notifications");
                return StatusCode(500, "An error occurred while fetching notifications.");
            }
        }
        [HttpGet("{userName}")]
        public async Task<ActionResult<IEnumerable<NotificationDto>>> GetAll(string userName)
        {
            try
            {
                // Verify the JWT user is the same person requesting their notifications
                var currentUserIdStr = User.FindFirstValue("uid");
                if (!int.TryParse(currentUserIdStr, out var currentUserId))
                    return Unauthorized("Invalid user ID in token.");

                var items = await _service.GetAllAsync(currentUserId);

                var dtos = items.Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Message = n.Message,
                    CreatedAt = n.CreatedAt,
                    IsRead = n.IsRead,
                    TargetUserId = n.TargetUserId
                }).ToList();

                return Ok(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all notifications");
                return StatusCode(500, "An error occurred while fetching notifications.");
            }
        }


        [HttpPost("mark-read")]
        public async Task<IActionResult> MarkRead()
        {
            try
            {
                var currentUserIdStr = User.FindFirstValue("uid");
                if (!int.TryParse(currentUserIdStr, out var currentUserId))
                    return Unauthorized("Invalid user ID in token.");

                await _service.MarkAllAsReadAsync(currentUserId);
                return Ok(new { message = "Notifications marked as read." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notifications as read");
                return StatusCode(500, "An error occurred while marking notifications as read.");
            }
        }

    }
}