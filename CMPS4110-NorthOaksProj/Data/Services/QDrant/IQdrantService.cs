using CMPS4110_NorthOaksProj.Data.Services.QDrant;

namespace CMPS4110_NorthOaksProj.Data.Services.QDrant
{
    public interface IQdrantService
    {
        Task<Guid> InsertVectorAsync(float[] embedding, int contractId, int chunkIndex);
        Task<List<VectorSearchResult>> SearchSimilarAsync(float[] queryEmbedding, int limit = 10, float scoreThreshold = 0.5f);
        Task DeleteVectorsByContractAsync(int contractId);
    }
}