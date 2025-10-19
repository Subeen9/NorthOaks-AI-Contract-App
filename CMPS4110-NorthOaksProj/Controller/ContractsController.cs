using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Hubs;
using CMPS4110_NorthOaksProj.Models.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;


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

        public ContractsController(
            IContractsService contractsService,
            IWebHostEnvironment env,
            IHubContext<ProcessingHub> hubContext)
        {
            _contractsService = contractsService;
            _env = env;
            _hubContext = hubContext;
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
        public async Task<ActionResult<ContractReadDto>> Upload([FromForm] ContractUploadDto dto, [FromServices] IBackgroundTaskQueue taskQueue)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.File == null || dto.File.Length == 0) return BadRequest("No file uploaded.");

            var userGroup = User.Identity?.Name ?? dto.UserId.ToString();
            await _hubContext.Clients.Group(userGroup).SendAsync("ProcessingUpdate", new
            {
                message = "Upload received. Starting processing...",
                progress = 10
            });


            var contract = await _contractsService.UploadContract(dto, _env.WebRootPath);

            taskQueue.QueueContractProcessing(contract.Id, _env.WebRootPath, userGroup, _hubContext);

            // reload with User so UploadedBy isn’t null
            var savedContract = await _contractsService.GetByIdWithUser(contract.Id);

            var readDto = ToReadDto(savedContract!);
            return CreatedAtAction(nameof(GetById), new { id = readDto.Id }, readDto);
        }

        // DELETE: api/contracts/{id}
        [HttpDelete("{id:int}")]
      //  [Authorize] 
        public async Task<ActionResult> Delete(int id)
        {
            var action = await _contractsService.DeleteContract(id, _env.ContentRootPath);
            if (!action) return NotFound();
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
