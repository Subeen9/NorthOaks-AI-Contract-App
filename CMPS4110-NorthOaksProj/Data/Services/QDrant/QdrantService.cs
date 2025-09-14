using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace CMPS4110_NorthOaksProj.Data.Services
{
    public class QdrantService : IQdrantService, IDisposable
    {
        private readonly QdrantClient _client;
        private readonly ILogger<QdrantService> _logger;
        private const string CollectionName = "contract_embeddings";

        public QdrantService(IConfiguration configuration, ILogger<QdrantService> logger)
        {
            _logger = logger;
            var qdrantUrl = configuration.GetConnectionString("Qdrant") ?? "http://localhost:6333";
            _client = new QdrantClient(qdrantUrl);

            EnsureCollectionExists().Wait();
        }

        private async Task EnsureCollectionExists()
        {
            try
            {
                var collections = await _client.ListCollectionsAsync();
                var collectionExists = collections.Contains(CollectionName);

                if (!collectionExists)
                {
                    var vectorParams = new VectorParams
                    {
                        Size = 384,
                        Distance = Distance.Cosine
                    };

                    await _client.CreateCollectionAsync(CollectionName, vectorParams);
                    _logger.LogInformation("Created Qdrant collection: {CollectionName}", CollectionName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure Qdrant collection exists");
                throw;
            }
        }

        public async Task<Guid> InsertVectorAsync(float[] embedding, int contractId, int chunkIndex)
        {
            var pointId = Guid.NewGuid();

            var point = new PointStruct
            {
                Id = new PointId { Uuid = pointId.ToString() },
                Vectors = embedding,
                Payload =
                {
                    ["contract_id"] = contractId,
                    ["chunk_index"] = chunkIndex,
                    ["created_at"] = DateTime.UtcNow.ToString("O")
                }
            };

            await _client.UpsertAsync(CollectionName, new[] { point });
            return pointId;
        }

        public async Task<List<VectorSearchResult>> SearchSimilarAsync(float[] queryEmbedding, int limit = 10, float scoreThreshold = 0.5f)
        {
            var searchResult = await _client.SearchAsync(
                collectionName: CollectionName,
                vector: queryEmbedding,
                limit: (ulong)limit,
                scoreThreshold: scoreThreshold
                
            );

            return searchResult.Select(point => new VectorSearchResult
            {
                PointId = Guid.Parse(point.Id.Uuid),
                Score = point.Score,
                ContractId = (int)point.Payload["contract_id"].IntegerValue,
                ChunkIndex = (int)point.Payload["chunk_index"].IntegerValue
            }).ToList();
        }

        public async Task DeleteVectorsByContractAsync(int contractId)
        {
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "contract_id",
                    Match = new Match { Integer = contractId }
                }
            });

            await _client.DeleteAsync(CollectionName, filter);
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }

    public class VectorSearchResult
    {
        public Guid PointId { get; set; }
        public float Score { get; set; }
        public int ContractId { get; set; }
        public int ChunkIndex { get; set; }
    }
}