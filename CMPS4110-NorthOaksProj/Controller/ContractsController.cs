using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Hubs;
using CMPS4110_NorthOaksProj.Models.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;


namespace CMPS4110_NorthOaksProj.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // [Authorize] //  require authentication for all endpoints by default
    public class ContractsController : ControllerBase
    {
        private readonly IContractsService _contractsService;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<ProcessingHub> _hubContext;
        private readonly IHubContext<NotificationHub> _notificationHub;


        public ContractsController(
            IContractsService contractsService,
            IWebHostEnvironment env,
            IHubContext<ProcessingHub> hubContext,
            IHubContext<NotificationHub> notificationHub)
        {
            _contractsService = contractsService;
            _env = env;
            _hubContext = hubContext;
            _notificationHub = notificationHub;
        }

        // GET: api/contracts
        [HttpGet]
      //   [Authorize] //  any logged-in user can view
        public async Task<ActionResult<IEnumerable<ContractReadDto>>> GetAll()
        {
            var contracts = await _contractsService.GetAllWithUser();
            return Ok(contracts.Select(ToReadDto));
        }

        // GET: api/contracts/{id}
        [HttpGet("{id:int}")]
      //   [Authorize] //  any logged-in user can view
        public async Task<ActionResult<ContractReadDto>> GetById(int id)
        {
            var contract = await _contractsService.GetByIdWithUser(id);
            if (contract == null) return NotFound();

            return Ok(ToReadDto(contract));
        }

        // POST: api/contracts/upload
        [HttpPost("upload")]
        //   [Authorize] //  any logged-in user can upload
        public async Task<ActionResult<ContractReadDto>> Upload(
    [FromForm] ContractUploadDto dto,
    [FromServices] IBackgroundTaskQueue taskQueue)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.File == null || dto.File.Length == 0) return BadRequest("No file uploaded.");

            // --- THIS IS THE FIX ---
            // Get the ID of the user who is *actually* uploading
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // This userGroup logic should also use the real ID
            var userGroup = currentUserId ?? "unknown_user";

            // ... (Your processing hub logic)
            await _hubContext.Clients.Group(userGroup).SendAsync("ProcessingUpdate", new
            {
                message = "Upload received. Starting processing...",
                progress = 10
            });

            var contract = await _contractsService.UploadContract(dto, _env.WebRootPath);
            taskQueue.QueueContractProcessing(contract.Id, _env.WebRootPath, userGroup, _hubContext);

            var savedContract = await _contractsService.GetByIdWithUser(contract.Id);
            var uploadedBy = savedContract?.User != null
                ? $"{savedContract.User.FirstName} {savedContract.User.LastName}"
                : "Unknown User";

            // --- SEND THE CORRECT ID ---
            await _notificationHub.Clients.All.SendAsync("ReceiveNotification", new
            {
                Message = $"Contract '{dto.File.FileName}' uploaded by {uploadedBy}.",
                UserId = currentUserId // Use the ID from the claim
            });

            var readDto = ToReadDto(savedContract!);
            return CreatedAtAction(nameof(GetById), new { id = readDto.Id }, readDto);
        }

        // DELETE: api/contracts/{id}
        [HttpDelete("{id:int}")]
        //  [Authorize] 

        public async Task<ActionResult> Delete(int id)
        {
            // --- 1. Get contract details FIRST ---
            // We need to get the contract object *before* it's soft-deleted.
            var contractToDel = await _contractsService.GetByIdWithUser(id);
            if (contractToDel == null) return NotFound("Contract not found.");

            // Save the file name for the notification message
            var contractFileName = contractToDel.FileName;

            // --- 2. Get the DELETOR's info from the token claims ---
            // (This is the same logic from your MainLayout)
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            var firstName = User.FindFirstValue("name") ?? "";
            var lastName = User.FindFirstValue("family_name") ?? "";
            var deletedByName = $"{firstName} {lastName}".Trim();

            // Fallback just in case claims are empty
            if (string.IsNullOrWhiteSpace(deletedByName))
            {
                deletedByName = "Unknown User";
            }

            // --- 3. Now, perform the soft delete ---
            var action = await _contractsService.DeleteContract(id, _env.ContentRootPath);
            if (!action) return BadRequest("Delete action failed."); // Should be covered by the first check

            // --- 4. Send the notification with the correct info ---
            await _notificationHub.Clients.All.SendAsync("ReceiveNotification", new
            {
                Message = $"Contract '{contractFileName}' deleted by {deletedByName}.",
                UserId = currentUserId // This is the ID of the deletor
            });

            return NoContent();
        }
        //  Mapping helper
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
