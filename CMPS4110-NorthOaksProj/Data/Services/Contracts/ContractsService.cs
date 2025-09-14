using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Models.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CMPS4110_NorthOaksProj.Data.Services
{
    public class ContractsService : EntityBaseRepository<Contract>, IContractsService
    {
        private readonly DataContext _context;

        public ContractsService(DataContext context) : base(context)
        {
            _context = context;
        }

        public async Task<Contract> UploadContract(ContractUploadDto dto, string rootPath)
        {
            var uploadsFolder = Path.Combine(rootPath, "UploadedContracts");
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
                OCRText = null,
                IsDeleted = false
            };

            await AddAsync(contract);
            return contract;
        }

        public async Task<bool> DeleteContract(int id, string rootPath)
        {
            var contract = await GetByIdAsync(id);
            if (contract == null) return false;

            // Soft delete instead of physical removal
            contract.IsDeleted = true;
            contract.DeletedAt = DateTime.Now;

            await UpdateAsync(id, contract);
            return true;
        }

        //  Get all contracts that are not soft-deleted, with user info
        public async Task<IEnumerable<Contract>> GetAllWithUser()
        {
            return await _context.Contracts
                .Where(c => !c.IsDeleted)
                .Include(c => c.User)
                .ToListAsync();
        }

        //  Get single contract if not soft-deleted, with user info
        public async Task<Contract?> GetByIdWithUser(int id)
        {
            return await _context.Contracts
                .Where(c => !c.IsDeleted && c.Id == id)
                .Include(c => c.User)
                .FirstOrDefaultAsync();
        }
    }
}
