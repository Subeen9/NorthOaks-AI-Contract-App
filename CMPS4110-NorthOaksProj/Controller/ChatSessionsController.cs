using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Hubs;
using CMPS4110_NorthOaksProj.Models.Chat;
using CMPS4110_NorthOaksProj.Models.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
//using CMPS4110_NorthOaksProj.Models.Chat.Dtos;
using NorthOaks.Shared.Model.Chat;

namespace CMPS4110_NorthOaksProj.Controller
{
    [ApiController]
    [Route("api/chatsessions")]
    public class ChatSessionsController : ControllerBase
    {
        private readonly DataContext _db;
        private readonly IHubContext<NotificationHub> _notificationHub;
        public ChatSessionsController(DataContext db, IHubContext<NotificationHub> notificationHub)
        {
            _db = db;
            _notificationHub = notificationHub;
        }


        [HttpGet]
        public async Task<ActionResult<IEnumerable<ChatSessionDto>>> GetAll()
        {
            var sessions = await _db.ChatSessions
                .Include(s => s.Messages)
                .Include(s => s.SessionContracts)
                .Select(s => new ChatSessionDto
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    CreatedDate = s.CreatedDate,
                    MessageCount = s.Messages.Count,
                    ContractIds = s.SessionContracts.Select(sc => sc.ContractId).ToList(),
                    IsPublic = s.IsPublic
                })
                .ToListAsync();

            return Ok(sessions);
        }


        [HttpGet("{id:int}")]
        public async Task<ActionResult<ChatSessionDto>> Get(int id)
        {
            var s = await _db.ChatSessions
                .Include(x => x.Messages)
                .Include(x => x.SessionContracts)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (s == null) return NotFound();

            return new ChatSessionDto
            {
                Id = s.Id,
                UserId = s.UserId,
                CreatedDate = s.CreatedDate,
                MessageCount = s.Messages.Count,
                ContractIds = s.SessionContracts.Select(sc => sc.ContractId).ToList(),
                IsPublic = s.IsPublic
            };
        }

        [HttpGet("user/{userId}")]
        public async Task<ActionResult<IEnumerable<ChatSessionDto>>> GetUserSessions(int userId)
        {
            var sessions = await _db.ChatSessions
                .Include(s => s.Messages)
                .Include(s => s.SessionContracts)
                    .ThenInclude(sc => sc.Contract)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.CreatedDate)
                .Select(s => new ChatSessionDto
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    CreatedDate = s.CreatedDate,
                    MessageCount = s.Messages.Count,
                    ContractIds = s.SessionContracts.Select(sc => sc.ContractId).ToList(),
                    Contracts = s.SessionContracts.Select(sc => new ContractInfoDto
                    {
                        Id = sc.Contract.Id,
                        FileName = sc.Contract.FileName
                    }).ToList(),
                    IsPublic = s.IsPublic
                })
                .ToListAsync();

            return Ok(sessions);
        }

        [HttpGet("public")]
        public async Task<ActionResult<IEnumerable<ChatSessionDto>>> GetPublicComparisons()
        {
            var sessions = await _db.ChatSessions
                .AsNoTracking()
                .Include(s => s.SessionContracts)
                    .ThenInclude(sc => sc.Contract)
                .Include(s => s.Messages)
                .Where(s => s.IsPublic && s.SessionType == ChatSessionType.Comparison)
                .OrderByDescending(s => s.CreatedDate)
                .Select(s => new ChatSessionDto
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    CreatedDate = s.CreatedDate,
                    MessageCount = s.Messages.Count,
                    ContractIds = s.SessionContracts.Select(sc => sc.ContractId).ToList(),
                    Contracts = s.SessionContracts.Select(sc => new ContractInfoDto
                    {
                        Id = sc.Contract.Id,
                        FileName = sc.Contract.FileName
                    }).ToList(),
                    IsPublic = s.IsPublic
                })
                .ToListAsync();

            return Ok(sessions);
        }

        [HttpPost]
        public async Task<ActionResult<ChatSessionDto>> Create(CreateChatSessionDto dto)
        {
            if (dto.ContractIds is { Count: > 2 })
                return BadRequest(new { error = "Maximum 2 contracts can be compared" });

            // reuse single-doc sessions, but always create new for comparisons
            if (dto.ContractIds is { Count: 1 })
            {
                var existing = await _db.ChatSessions
                    .Include(s => s.Messages)
                    .Include(s => s.SessionContracts)
                    .Where(s => s.UserId == dto.UserId)
                    .FirstOrDefaultAsync(s => s.SessionContracts.Any(sc => dto.ContractIds.Contains(sc.ContractId)));

                if (existing != null)
                {
                    var existingDto = new ChatSessionDto
                    {
                        Id = existing.Id,
                        UserId = existing.UserId,
                        CreatedDate = existing.CreatedDate,
                        MessageCount = existing.Messages.Count,
                        ContractIds = existing.SessionContracts.Select(sc => sc.ContractId).ToList(),
                        SessionType = (int)existing.SessionType,
                        IsPublic = existing.IsPublic
                    };
                    return Ok(existingDto);
                }
            }

            var entity = new ChatSession
            {
                UserId = dto.UserId,
                SessionType = (dto.ContractIds?.Count ?? 0) > 1 ? ChatSessionType.Comparison : ChatSessionType.Single
            };
            _db.ChatSessions.Add(entity);

            if (dto.ContractIds is { Count: > 0 })
            {
                foreach (var cid in dto.ContractIds.Distinct())
                {
                    _db.ChatSessionContracts.Add(new ChatSessionContract
                    {
                        ChatSession = entity,
                        ContractId = cid
                    });
                }
            }

            await _db.SaveChangesAsync();

            var result = new ChatSessionDto
            {
                Id = entity.Id,
                UserId = entity.UserId,
                CreatedDate = entity.CreatedDate,
                MessageCount = 0,
                ContractIds = dto.ContractIds ?? new List<int>(),
                SessionType = (int)entity.SessionType
            };

            return CreatedAtAction(nameof(Get), new { id = entity.Id }, result);
        }

        [HttpPut("comparisons/{id:int}/visibility")]
        public async Task<IActionResult> SetComparisonVisibility(int id, [FromQuery] bool isPublic)
        {
            var currentUserName = User.Identity?.Name?.ToLower() ?? "unknown_user";

            // Get the current user
            var currentUser = await _db.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);

            if (currentUser == null)
                return Unauthorized();

            // Get the comparison session with contracts
            var session = await _db.ChatSessions
                .Include(s => s.SessionContracts)
                    .ThenInclude(sc => sc.Contract)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (session == null)
                return NotFound("Comparison session not found.");

            // Ensure this is a comparison session
            if (session.SessionType != ChatSessionType.Comparison)
                return BadRequest("This endpoint only modifies comparison sessions.");

            // Ensure only the owner can change visibility
            if (session.UserId != currentUser.Id)
                return Forbid("Cannot modify another user's comparison session.");

            bool wasPrivate = !session.IsPublic;
            session.IsPublic = isPublic;
            await _db.SaveChangesAsync();

            // Notify others if it just became public
            if (wasPrivate && isPublic)
            {
                var title = session.SessionContracts.Any()
                    ? string.Join(" vs ", session.SessionContracts.Select(sc => sc.Contract.FileName))
                    : "Contract Comparison";

                var ownerName = $"{currentUser.FirstName} {currentUser.LastName}";

                var targetUsers = await _db.Users
                    .Where(u => u.Id != currentUser.Id)
                    .ToListAsync();

                foreach (var user in targetUsers)
                {
                    _db.Notifications.Add(new Notification
                    {
                        Message = $"Comparison '{title}' is now PUBLIC (by {ownerName}).",
                        TargetUserId = user.Id,
                        CreatedAt = DateTime.UtcNow,
                        IsRead = false
                    });
                }

                await _db.SaveChangesAsync();

                var targetGroups = targetUsers.Select(u => u.UserName.Trim().ToLower()).ToList();

                await _notificationHub.Clients.Groups(targetGroups).SendAsync("ReceiveNotification", new
                {
                    Message = $"Comparison '{title}' is now PUBLIC.",
                    UserId = currentUserName,
                    CreatedAt = DateTime.UtcNow
                });
            }

            return Ok(new
            {
                message = $"Visibility updated to {(isPublic ? "PUBLIC" : "PRIVATE")}"
            });
        }



        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var session = await _db.ChatSessions.FindAsync(id);
            if (session == null) return NotFound();

            // Get current user's username from claims
            var currentUserName = User.Identity?.Name?.ToLower() ?? "unknown_user";

            var currentUser = await _db.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);

            if (currentUser == null)
                return Unauthorized();

            // Check if the current user is the owner
            if (session.UserId != currentUser.Id)
                return Forbid("You cannot delete another user's session.");

            _db.ChatSessions.Remove(session);
            await _db.SaveChangesAsync();

            return NoContent();
        }

    }
}
