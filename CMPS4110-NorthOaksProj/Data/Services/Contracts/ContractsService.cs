using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Models.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CMPS4110_NorthOaksProj.Data.Services
{
    public class ContractsService : EntityBaseRepository<Contract>, IContractsService
    {
        private readonly DataContext _context;
        private readonly IDocumentProcessingService _documentProcessing;
        private readonly ILogger<ContractsService> _logger;

        public ContractsService(
            DataContext context,
            IDocumentProcessingService documentProcessing,
            ILogger<ContractsService> logger)
            : base(context)
        {
            _context = context;
            _documentProcessing = documentProcessing;
            _logger = logger;
        }

        public async Task<Contract> UploadContract(ContractUploadDto dto, string rootPath)
        {
            try
            {
                if (dto.File == null || dto.File.Length == 0)
                    throw new ArgumentException("File is required");

                var uploadsFolder = Path.Combine(rootPath, "UploadedContracts");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                // ✅ Generate a unique saved filename
                var savedFileName = $"{Guid.NewGuid()}_{dto.File.FileName}";
                var filePath = Path.Combine(uploadsFolder, savedFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await dto.File.CopyToAsync(stream);
                }

                var contract = new Contract
                {
                    FileName = savedFileName,   // ✅ store actual saved file name
                    UploadDate = DateTime.Now,
                    UserId = dto.UserId,
                    IsDeleted = false
                };

                await AddAsync(contract);

                // Optional: OCR processing
                await _documentProcessing.ProcessDocumentAsync(contract.Id, filePath);

                return contract;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading contract for user {UserId}", dto.UserId);
                throw;
            }
        }

        public async Task<bool> DeleteContract(int id, string rootPath)
        {
            try
            {
                var contract = await GetByIdAsync(id);
                if (contract == null) return false;

                contract.IsDeleted = true;
                contract.DeletedAt = DateTime.Now;
                await UpdateAsync(id, contract);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting contract", id);
                return false;
            }
        }

        public async Task<IEnumerable<Contract>> GetAllWithUser()
        {
            return await _context.Contracts
                .Where(c => !c.IsDeleted)
                .Include(c => c.User)
                .ToListAsync();
        }

        public async Task<Contract?> GetByIdWithUser(int id)
        {
            return await _context.Contracts
                .Where(c => !c.IsDeleted && c.Id == id)
                .Include(c => c.User)
                .FirstOrDefaultAsync();
        }
    }
}
