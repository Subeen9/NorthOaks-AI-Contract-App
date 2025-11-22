using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Models.Contracts;

namespace CMPS4110_NorthOaksProj.Data.Services.Contracts
{
    public interface IContractsService : IEntityBaseRepository<Contract>
    {
        Task<Contract> UploadContract(ContractUploadDto dto, string rootPath);
        Task<bool> DeleteContract(int id, string rootPath);

       
        Task<IEnumerable<Contract>> GetAllWithUser(int currentUserId);
        Task<Contract?> GetByIdWithUser(int id, int currentUserId);
        Task ProcessContractAsync(int contractId, string rootPath, CancellationToken token, Func<int, string, Task>? progressCallback = null);

        string GetOriginalFileName(string dbFileName);
    }
    
    }

