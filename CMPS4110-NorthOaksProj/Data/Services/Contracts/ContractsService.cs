
using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Data.Services.Contracts;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Models.Contracts;


namespace CMPS4110_NorthOaksProj.Data.Services
{
    public class ContractsService : EntityBaseRepository<Contract>, IContractsService
    {
        private readonly IDocumentProcessingService _documentProcessing;
        private readonly ILogger<ContractsService> _logger;

        public ContractsService(DataContext context, IDocumentProcessingService documentProcessing, ILogger<ContractsService> logger) : base(context)
        {
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

                var fileName = $"{Guid.NewGuid()}_{dto.File.FileName}";
                var filePath = Path.Combine(uploadsFolder, fileName);

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading contract", dto.UserId);
                throw;
            }
        }
        public async Task<bool> DeleteContract(int id, string rootPath)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting contract", id);
                return false;
            }
        }
    }
}
