using CMPS4110_NorthOaksProj.Data.Services.QDrant;

namespace CMPS4110_NorthOaksProj.Data.Services.QDrant
{
    public interface IQdrantService
    {
        Task<Guid> InsertVectorAsync(float[] embedding, int contractId, int chunkIndex, string chuntText, int pageNumber);
        Task<List<VectorSearchResult>> SearchSimilarAsync(float[] queryEmbedding, int limit = 20, float scoreThreshold = 0.15f);
        Task DeleteVectorsByContractAsync(int contractId);
    }
}