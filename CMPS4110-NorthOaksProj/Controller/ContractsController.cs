using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Models.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace CMPS4110_NorthOaksProj.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
        public async Task<ActionResult<IEnumerable<ContractReadDto>>> GetAll()
        {
            var contracts = await _contractsService.GetAll();

            var dtos = contracts.Select(c => new ContractReadDto
            {
                Id = c.Id,
                FileName = c.FileName,
                UploadDate = c.UploadDate,
                UserId = c.UserId,
                UploadedBy = c.User != null ? $"{c.User.FirstName} {c.User.LastName}" : null
            });

            return Ok(dtos);
        }

        // GET: api/contracts/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ContractReadDto>> GetById(int id)
        {
            var contract = await _contractsService.GetByIdAsync(id);

            if (contract == null)
                return NotFound();

            var dto = new ContractReadDto
            {
                Id = contract.Id,
                FileName = contract.FileName,
                UploadDate = contract.UploadDate,
                UserId = contract.UserId,
                UploadedBy = contract.User != null ? $"{contract.User.FirstName} {contract.User.LastName}" : null
            };

            return Ok(dto);
        }

        // POST: api/contracts/upload
        [HttpPost("upload")]
        public async Task<ActionResult<ContractReadDto>> Upload([FromForm] ContractUploadDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (dto.File == null || dto.File.Length == 0)
                return BadRequest("No file uploaded.");

            var contract = await _contractsService.UploadContract(dto, _env.ContentRootPath);

            var readDto = new ContractReadDto
            {
                Id = contract.Id,
                FileName = contract.FileName,
                UploadDate = contract.UploadDate,
                UserId = contract.UserId,
                UploadedBy = null // won't be available until we load User
            };

            return CreatedAtAction(nameof(GetById), new { id = contract.Id }, readDto);
        }

        // DELETE: api/contracts/{id}
        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            var action = await _contractsService.DeleteContract(id, _env.ContentRootPath);
            if (!action) return NotFound();
            return NoContent();
        }
    }
}
