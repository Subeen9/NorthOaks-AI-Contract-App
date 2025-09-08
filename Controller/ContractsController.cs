using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Models.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace CMPS4110_NorthOaksProj.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContractsController : ControllerBase
    {
        private readonly IEntityBaseRepository<Contract> _contractRepository;
        private readonly IWebHostEnvironment _env;

        public ContractsController(
            IEntityBaseRepository<Contract> contractRepository,
            IWebHostEnvironment env)
        {
            _contractRepository = contractRepository;
            _env = env;
        }

        // GET: api/contracts
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ContractReadDto>>> GetAll()
        {
            var contracts = await _contractRepository.GetAll();

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
            var contract = await _contractRepository.GetByIdAsync(id);

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

            var uploadsFolder = Path.Combine(_env.ContentRootPath, "UploadedContracts");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var filePath = Path.Combine(uploadsFolder, dto.File.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await dto.File.CopyToAsync(stream);
            }

            var contract = new Contract
            {
                FileName = dto.File.FileName,
                UploadDate = DateTime.Now,
                UserId = dto.UserId,
                OCRText = null
            };

            await _contractRepository.AddAsync(contract);

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
            var contract = await _contractRepository.GetByIdAsync(id);
            if (contract == null)
                return NotFound();

            var uploadsFolder = Path.Combine(_env.ContentRootPath, "UploadedContracts");
            var filePath = Path.Combine(uploadsFolder, contract.FileName);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            await _contractRepository.DeleteAsync(id);

            return NoContent();
        }
    }
}
