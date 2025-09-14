
using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Models.Contracts;


namespace CMPS4110_NorthOaksProj.Data.Services
{
    public class ContractsService : EntityBaseRepository<Contract>, IContractsService
    {
        private readonly IDocumentProcessingService _documentProcessing;

        public ContractsService(DataContext context, IDocumentProcessingService documentProcessing) : base(context) { 
            _documentProcessing = documentProcessing;
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
               //OCRText = null
            };

            await AddAsync(contract);
            await _documentProcessing.ProcessDocumentAsync(contract.Id, filePath);
            return contract;
        }

        public async Task<bool> DeleteContract(int id, string rootPath)
        {
            var contract = await GetByIdAsync(id);
            if (contract == null) return false;

            var filePath = Path.Combine(rootPath, "UploadedContracts", contract.FileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            await DeleteAsync(id);
            return true;
        }
    }
}
