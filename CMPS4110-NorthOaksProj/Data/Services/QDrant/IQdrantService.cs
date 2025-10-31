using CMPS4110_NorthOaksProj.Data.Services.QDrant;

namespace CMPS4110_NorthOaksProj.Data.Services.QDrant
{
    public interface IQdrantService
    {
        Task<Guid> InsertVectorAsync(float[] embedding, int contractId, int chunkIndex, string chuntText, int pageNumber);
        // Add optional contractIds to restrict search to only those contract(s)
        Task<List<VectorSearchResult>> SearchSimilarAsync(float[] queryEmbedding, int limit = 20, float scoreThreshold = 0.15f, IEnumerable<int>? contractIds = null);
        Task DeleteVectorsByContractAsync(int contractId);
    }
}