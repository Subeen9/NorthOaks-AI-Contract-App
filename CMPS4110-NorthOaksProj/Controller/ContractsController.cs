using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Hubs;
using CMPS4110_NorthOaksProj.Models.Contracts;
using CMPS4110_NorthOaksProj.Models.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CMPS4110_NorthOaksProj.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ContractsController : ControllerBase
    {
        private readonly IContractsService _contractsService;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ProcessingHub> _hubContext;
        private readonly IHubContext<NotificationHub> _notificationHub;
        private readonly DataContext _context;

        public ContractsController(
            IContractsService contractsService,
            IWebHostEnvironment env,
            IHubContext<ProcessingHub> hubContext,
            IHubContext<NotificationHub> notificationHub,
            DataContext context)
        {
            _contractsService = contractsService;
            _env = env;
            _hubContext = hubContext;
            _notificationHub = notificationHub;
            _context = context;
        }

        // ====================================================================
        //  POST: Upload contract
        // ====================================================================
        [HttpPost("upload")]
        public async Task<ActionResult<ContractReadDto>> Upload(
            [FromForm] ContractUploadDto dto,
            [FromServices] IBackgroundTaskQueue taskQueue)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.File == null || dto.File.Length == 0) return BadRequest("No file uploaded.");

            // Resolve current user
            var currentUserName =
                User.FindFirstValue("preferred_username") ??
                User.FindFirstValue("unique_name") ??
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.Identity?.Name ??
                "unknown_user";

            var uploader = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);

            if (uploader == null)
                return Unauthorized("Invalid user.");

            // Send processing update only to uploader
            await _hubContext.Clients.Group(currentUserName).SendAsync("ProcessingUpdate", new
            {
                message = "Upload received. Starting processing...",
                progress = 10
            });

            // Save file + create DB record
            var contract = await _contractsService.UploadContract(dto, _env.WebRootPath);

            // Queue background processing
            taskQueue.QueueContractProcessing(contract.Id, _env.WebRootPath, currentUserName, _hubContext);

            // Load saved contract
            var savedContract = await _context.Contracts
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == contract.Id);

            var uploadedBy = $"{uploader.FirstName} {uploader.LastName}";

            // ============================================================
            //  NOTIFY ONLY IF PUBLIC
            // ============================================================
            if (!savedContract.IsPublic)
            {
                Console.WriteLine("[UPLOAD] Private contract → No notifications sent.");
                return CreatedAtAction(nameof(GetById), new { id = savedContract.Id }, ToReadDto(savedContract));
            }

            // Send persistent notifications to all other users
            var targetUsers = await _context.Users
                .Where(u => u.Id != uploader.Id)
                .ToListAsync();

            foreach (var user in targetUsers)
            {
                _context.Notifications.Add(new Notification
                {
                    Message = $"Contract '{dto.File.FileName}' uploaded by {uploadedBy}.",
                    TargetUserId = user.Id,
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                });
            }

            await _context.SaveChangesAsync();

            // Real-time notifications
            var targetGroups = targetUsers.Select(u => u.UserName.Trim().ToLower()).ToList();

            await _notificationHub.Clients.Groups(targetGroups).SendAsync("ReceiveNotification", new
            {
                Message = $"Contract '{dto.File.FileName}' uploaded by {uploadedBy}.",
                UserId = currentUserName,
                CreatedAt = DateTime.UtcNow
            });

            return CreatedAtAction(nameof(GetById), new { id = savedContract.Id }, ToReadDto(savedContract));
        }


        // ====================================================================
        //  DELETE contract
        // ====================================================================
        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            var currentUserName =
                User.FindFirstValue("preferred_username") ??
                User.FindFirstValue("unique_name") ??
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.Identity?.Name ??
                "unknown_user";

            var deletor = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);

            if (deletor == null)
                return Unauthorized("Invalid user.");

            var contract = await _context.Contracts
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (contract == null)
                return NotFound("Contract not found.");

            var fileName = _contractsService.GetOriginalFileName(contract.FileName);
            var deletedBy = $"{deletor.FirstName} {deletor.LastName}";

            // Perform delete
            var action = await _contractsService.DeleteContract(id, _env.ContentRootPath);
            if (!action) return BadRequest("Delete action failed.");

            // ============================================================
            //  PRIVATE → Skip notifications
            // ============================================================
            if (!contract.IsPublic)
            {
                Console.WriteLine("[DELETE] Private contract → No notifications sent.");
                return NoContent();
            }

            // Persistent notifications to others
            var targetUsers = await _context.Users
                .Where(u => u.Id != deletor.Id)
                .ToListAsync();

            foreach (var user in targetUsers)
            {
                _context.Notifications.Add(new Notification
                {
                    Message = $"Contract '{fileName}' deleted by {deletedBy}.",
                    TargetUserId = user.Id,
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                });
            }

            await _context.SaveChangesAsync();

            // Real-time notifications
            var targetGroups = targetUsers.Select(u => u.UserName.Trim().ToLower()).ToList();

            await _notificationHub.Clients.Groups(targetGroups).SendAsync("ReceiveNotification", new
            {
                Message = $"Contract '{fileName}' deleted by {deletedBy}.",
                UserId = currentUserName,
                CreatedAt = DateTime.UtcNow
            });

            return NoContent();
        }


        // ====================================================================
        //  TOGGLE VISIBILITY (private <-> public)
        // ====================================================================
        [HttpPut("{id:int}/visibility")]
        public async Task<ActionResult> SetVisibility(int id, [FromQuery] bool isPublic)
        {
            var currentUserName = User.Identity?.Name?.ToLower() ?? "unknown_user";

            var currentUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);

            if (currentUser == null)
                return Unauthorized();

            var contract = await _context.Contracts.FindAsync(id);
            if (contract == null)
                return NotFound("Contract not found.");

            if (contract.UserId != currentUser.Id)
                return Forbid("Cannot modify another user's contract.");

            bool wasPrivate = !contract.IsPublic;

            contract.IsPublic = isPublic;
            await _context.SaveChangesAsync();

            // ======================================================
            //  If contract JUST NOW became PUBLIC → Notify everyone
            // ======================================================
            if (wasPrivate && isPublic)
            {
                var fileName = _contractsService.GetOriginalFileName(contract.FileName);
                var ownerName = $"{currentUser.FirstName} {currentUser.LastName}";

                var targetUsers = await _context.Users
                    .Where(u => u.Id != currentUser.Id)
                    .ToListAsync();

                foreach (var user in targetUsers)
                {
                    _context.Notifications.Add(new Notification
                    {
                        Message = $"Contract '{fileName}' is now PUBLIC (by {ownerName}).",
                        TargetUserId = user.Id,
                        CreatedAt = DateTime.UtcNow,
                        IsRead = false
                    });
                }

                await _context.SaveChangesAsync();

                var targetGroups = targetUsers.Select(u => u.UserName.Trim().ToLower()).ToList();

                await _notificationHub.Clients.Groups(targetGroups).SendAsync("ReceiveNotification", new
                {
                    Message = $"Contract '{fileName}' is now PUBLIC.",
                    UserId = currentUserName,
                    CreatedAt = DateTime.UtcNow
                });
            }

            return Ok(new
            {
                message = $"Visibility updated to {(isPublic ? "PUBLIC" : "PRIVATE")}"
            });
        }


        // ====================================================================
        //  GET CONTRACT BY ID
        // ====================================================================
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ContractReadDto>> GetById(int id)
        {
            var currentUserName = User.Identity?.Name?.ToLower() ?? "unknown_user";

            var currentUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);

            if (currentUser == null) return Unauthorized();

            var contract = await _contractsService.GetByIdWithUser(id, currentUser.Id);
            if (contract == null) return Forbid();

            return Ok(ToReadDto(contract));
        }


        // ====================================================================
        //  GET ALL CONTRACTS
        // ====================================================================
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ContractReadDto>>> GetAll()
        {
            var currentUserName = User.Identity?.Name?.ToLower() ?? "unknown_user";

            var currentUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);

            if (currentUser == null) return Unauthorized();

            var contracts = await _contractsService.GetAllWithUser(currentUser.Id);
            return Ok(contracts.Select(ToReadDto));
        }


        // ====================================================================
        //  Helper: Convert Contract → DTO
        // ====================================================================
        private static ContractReadDto ToReadDto(Contract contract)
        {
            return new ContractReadDto
            {
                Id = contract.Id,
                FileName = contract.FileName,
                UploadDate = contract.UploadDate,
                UserId = contract.UserId,
                UploadedBy = contract.User != null
                    ? $"{contract.User.FirstName} {contract.User.LastName}"
                    : null,
                FileUrl = $"/UploadedContracts/{contract.FileName}",
                IsPublic = contract.IsPublic
            };
        }
    }
}
