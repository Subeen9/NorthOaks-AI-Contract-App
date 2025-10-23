using CMPS4110_NorthOaksProj.Data;
using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Hubs;
using CMPS4110_NorthOaksProj.Models.Contracts;
using CMPS4110_NorthOaksProj.Models.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;


namespace CMPS4110_NorthOaksProj.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
     [Authorize] // enable later in production
    public class ContractsController : ControllerBase
    {
        private readonly IContractsService _contractsService;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ProcessingHub> _hubContext;
        private readonly IHubContext<NotificationHub> _notificationHub;
        private readonly DataContext _context; // ✅ EF context for saving notifications

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

        // GET: api/contracts
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ContractReadDto>>> GetAll()
        {
            var contracts = await _contractsService.GetAllWithUser();
            return Ok(contracts.Select(ToReadDto));
        }

        // GET: api/contracts/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ContractReadDto>> GetById(int id)
        {
            var contract = await _contractsService.GetByIdWithUser(id);
            if (contract == null) return NotFound();
            return Ok(ToReadDto(contract));
        }

        // POST: api/contracts/upload
        [HttpPost("upload")]
        public async Task<ActionResult<ContractReadDto>> Upload(
            [FromForm] ContractUploadDto dto,
            [FromServices] IBackgroundTaskQueue taskQueue)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.File == null || dto.File.Length == 0) return BadRequest("No file uploaded.");
            var currentUserId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var userGroup = currentUserId ?? "unknown_user";

            // Processing notification
            await _hubContext.Clients.Group(userGroup).SendAsync("ProcessingUpdate", new
            {
                message = "Upload received. Starting processing...",
                progress = 10
            });

            // Upload file
            var contract = await _contractsService.UploadContract(dto, _env.WebRootPath);
            taskQueue.QueueContractProcessing(contract.Id, _env.WebRootPath, userGroup, _hubContext);

            var savedContract = await _contractsService.GetByIdWithUser(contract.Id);
            var uploadedBy = savedContract?.User != null
                ? $"{savedContract.User.FirstName} {savedContract.User.LastName}"
                : "Unknown User";

            // ✅ Save persistent notifications for all users except uploader
            var uploaderIdInt = int.TryParse(currentUserId, out var uid) ? uid : 0;
            var targetUsers = await _context.Users
                .Where(u => u.Id != uploaderIdInt)
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

            // ✅ Real-time SignalR broadcast
            await _notificationHub.Clients.All.SendAsync("ReceiveNotification", new
            {
                Message = $"Contract '{dto.File.FileName}' uploaded by {uploadedBy}.",
                UserId = currentUserId
            });

            return CreatedAtAction(nameof(GetById), new { id = savedContract.Id }, ToReadDto(savedContract));
        }

        // DELETE: api/contracts/{id}
        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            var contractToDel = await _contractsService.GetByIdWithUser(id);
            if (contractToDel == null) return NotFound("Contract not found.");

            var fileName = contractToDel.FileName;
            var currentUserId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var firstName = User.FindFirstValue("name") ?? "";
            var lastName = User.FindFirstValue("family_name") ?? "";
            var deletedByName = $"{firstName} {lastName}".Trim();

            if (string.IsNullOrWhiteSpace(deletedByName))
                deletedByName = "Unknown User";

            var action = await _contractsService.DeleteContract(id, _env.ContentRootPath);
            if (!action) return BadRequest("Delete action failed.");

            // ✅ Save persistent delete notification
            var deletorIdInt = int.TryParse(currentUserId, out var did) ? did : 0;
            var targetUsers = await _context.Users
                .Where(u => u.Id != deletorIdInt)
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

            // ✅ SignalR push
            await _notificationHub.Clients.All.SendAsync("ReceiveNotification", new
            {
                Message = $"Contract '{fileName}' deleted by {deletedByName}.",
                UserId = currentUserId
            });

            return NoContent();
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
                FileUrl = $"/UploadedContracts/{contract.FileName}"
            };
        }

        [HttpGet("{id}/status")]
        public async Task<ActionResult<object>> GetStatus(int id)
        {
            var contract = await _contractsService.GetByIdWithUser(id);
            if (contract == null) return NotFound();

            return Ok(new
            {
                contract.Id,
                contract.IsProcessed,
                contract.ProcessingStatus
            });
        }
    }
}
