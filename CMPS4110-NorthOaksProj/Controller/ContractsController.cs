using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Hubs;
using CMPS4110_NorthOaksProj.Models.Contracts;
using CMPS4110_NorthOaksProj.Models.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

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

        // ===============================================================
        // POST: api/contracts/upload
        // ===============================================================
        [HttpPost("upload")]
        public async Task<ActionResult<ContractReadDto>> Upload(
            [FromForm] ContractUploadDto dto,
            [FromServices] IBackgroundTaskQueue taskQueue)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.File == null || dto.File.Length == 0) return BadRequest("No file uploaded.");

            // Get current username from token (normalized for group match)
            var currentUserName =
             User.FindFirstValue("preferred_username") ??
             User.FindFirstValue("unique_name") ??
             User.FindFirstValue(ClaimTypes.NameIdentifier) ??   //Had to do this to get the username correctly. Was having issue. 
             User.FindFirstValue("sub") ??
              User.Identity?.Name ??
               "unknown_user";

            Console.WriteLine($"[DEBUG BACKEND] currentUserName = {currentUserName}");

            var currentUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);

            // Notify uploader (progress bar)
            await _hubContext.Clients.Group(currentUserName).SendAsync("ProcessingUpdate", new
            {
                message = "Upload received. Starting processing...",
                progress = 10
            });

            // Upload the contract file
            var contract = await _contractsService.UploadContract(dto, _env.WebRootPath);
            taskQueue.QueueContractProcessing(contract.Id, _env.WebRootPath, currentUserName, _hubContext);

            var savedContract = await _contractsService.GetByIdWithUser(contract.Id, currentUser.Id);
            var uploadedBy = savedContract?.User != null
                ? $"{savedContract.User.FirstName} {savedContract.User.LastName}"
                : "Unknown User";

            // Save persistent notifications for all other users
            var uploader = await _context.Users.FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);
            int? uploaderId = uploader?.Id;

            var targetUsers = await _context.Users
                .Where(u => uploaderId == null || u.Id != uploaderId.Value)
                .ToListAsync();

            foreach (var user in targetUsers)
            {
                _context.Notifications.Add(new Notification
                {
                    Message = $"Contract '{dto.File.FileName}' uploaded by {uploadedBy}.",
                    TargetUserId = user.Id,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            // === Real-time SignalR broadcast (skip self) ===
            var targetGroups = targetUsers.Select(u => u.UserName.Trim().ToLower()).ToList();

            Console.WriteLine("=== DEBUG BROADCAST ===");
            Console.WriteLine($"Current user (from token): sub = '{User.FindFirstValue("sub")}', uid = '{User.FindFirstValue("uid")}'");
            Console.WriteLine($"Normalized currentUserName = '{currentUserName}'");
            Console.WriteLine($"Target groups being sent to: {string.Join(", ", targetGroups)}");
            Console.WriteLine("========================");

            await _notificationHub.Clients.Groups(targetGroups).SendAsync("ReceiveNotification", new
            {
                Message = $"Contract '{dto.File.FileName}' uploaded by {uploadedBy}.",
                UserId = currentUserName,  // same type as frontend _currentUserName
                CreatedAt = DateTime.UtcNow
            });

            return CreatedAtAction(nameof(GetById), new { id = savedContract.Id }, ToReadDto(savedContract));
        }

        // ===============================================================
        // DELETE: api/contracts/{id}
        // ===============================================================
        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            var currentUserName =
             User.FindFirstValue("preferred_username") ??
              User.FindFirstValue("unique_name") ??
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??   // <— added this
              User.FindFirstValue("sub") ??
            User.Identity?.Name ??
            "unknown_user";
            var currentUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);

            var contractToDel = await _contractsService.GetByIdWithUser(id, currentUser.Id);
            if (contractToDel == null) return NotFound("Contract not found.");

            var fileName = contractToDel.FileName;

            Console.WriteLine($"[DEBUG BACKEND] currentUserName = {currentUserName}");



            var firstName = User.FindFirstValue("name") ?? "";
            var lastName = User.FindFirstValue("family_name") ?? "";
            var deletedByName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrWhiteSpace(deletedByName)) deletedByName = "Unknown User";

            var action = await _contractsService.DeleteContract(id, _env.ContentRootPath);
            if (!action) return BadRequest("Delete action failed.");

            // Save persistent notifications for all other users
            var deletor = await _context.Users.FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName);
            int? deletorId = deletor?.Id;

            var targetUsers = await _context.Users
                .Where(u => deletorId == null || u.Id != deletorId.Value)
                .ToListAsync();

            foreach (var user in targetUsers)
            {
                _context.Notifications.Add(new Notification
                {
                    Message = $"Contract '{fileName}' deleted by {deletedByName}.",
                    TargetUserId = user.Id,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            // === Real-time SignalR broadcast (skip self) ===
            var targetGroups = targetUsers.Select(u => u.UserName.Trim().ToLower()).ToList();

            Console.WriteLine("=== DEBUG BROADCAST ===");
            Console.WriteLine($"Current user (from token): sub = '{User.FindFirstValue("sub")}', uid = '{User.FindFirstValue("uid")}'");
            Console.WriteLine($"Normalized currentUserName = '{currentUserName}'");
            Console.WriteLine($"Target groups being sent to: {string.Join(", ", targetGroups)}");
            Console.WriteLine("========================");

            await _notificationHub.Clients.Groups(targetGroups).SendAsync("ReceiveNotification", new
            {
                Message = $"Contract '{fileName}' deleted by {deletedByName}.",
                UserId = currentUserName,
                CreatedAt = DateTime.UtcNow
            });

            return NoContent();
        }

        // ===============================================================
        // GET: api/contracts/{id}
        // ===============================================================
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

        [HttpPut("{id:int}/visibility")]
        public async Task<ActionResult> SetVisibility(int id, [FromQuery] bool isPublic)
        {
            var currentUserName = User.Identity?.Name?.ToLower() ?? "unknown_user";
            var currentUser = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == currentUserName); if (currentUser == null) return Unauthorized();

            var contract = await _context.Contracts.FindAsync(id);
            if (contract == null) return NotFound();

            if (contract.UserId != currentUser.Id)
                return Forbid("You cannot change visibility for someone else’s contract.");

            contract.IsPublic = isPublic;
            await _context.SaveChangesAsync();
            await _notificationHub.Clients.All.SendAsync("ContractVisibilityChanged", new
            {
                ContractId = contract.Id,
                isPublic = contract.IsPublic
            });


            return Ok(new { message = $"Visibility set to {(isPublic ? "public" : "private")}" });
        }

    }
}
