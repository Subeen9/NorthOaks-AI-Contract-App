using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Models.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace CMPS4110_NorthOaksProj.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // [Authorize] //  require authentication for all endpoints by default
    public class ContractsController : ControllerBase
    {
        private readonly IContractsService _contractsService;
        private readonly IWebHostEnvironment _env;

        public ContractsController(
            IContractsService contractsService,
            IWebHostEnvironment env)
        {
            _contractsService = contractsService;
            _env = env;
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
        public async Task<ActionResult<ContractReadDto>> Upload([FromForm] ContractUploadDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.File == null || dto.File.Length == 0) return BadRequest("No file uploaded.");

            var contract = await _contractsService.UploadContract(dto, _env.ContentRootPath);

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
                    : null
            };
        }
    }
}
