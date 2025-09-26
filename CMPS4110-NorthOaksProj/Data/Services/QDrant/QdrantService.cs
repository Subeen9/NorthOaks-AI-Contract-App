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
        private bool _initialized = false;

        public QdrantService(IConfiguration configuration, ILogger<QdrantService> logger)
        {
            _logger = logger;

            // Gets the connection string
            var qdrantConnectionString = configuration.GetConnectionString("Qdrant") ?? "http://localhost:6333";

            try
            {
                // Parses the connection string to extract host and port
                var uri = new Uri(qdrantConnectionString);
                var host = uri.Host;
                var port = uri.Port;

                // Default ports: 6333:6334
                // QdrantClient uses gRPC by default
                var grpcPort = port == 6333 ? 6334 : port;

                _logger.LogInformation("Connecting to Qdrant at {Host}:{Port}", host, grpcPort);

                // Create client with host and gRPC port
                if (uri.Scheme == "https")
                {
                    // For HTTPS/TLS connections
                    var channel = QdrantChannel.ForAddress($"https://{host}:{grpcPort}");
                    var grpcClient = new QdrantGrpcClient(channel);
                    _client = new QdrantClient(grpcClient);
                }
                else
                {
                    // For HTTP connections (local development)
                    _client = new QdrantClient(host, grpcPort);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Qdrant client with connection string: {ConnectionString}", qdrantConnectionString);
                throw new InvalidOperationException($"Invalid Qdrant connection string: {qdrantConnectionString}", ex);
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (!_initialized)
            {
                await EnsureCollectionExists();
                _initialized = true;
            }
        }

        private async Task EnsureCollectionExists()
        {
            try
            {
                _logger.LogInformation("Checking if collection '{CollectionName}' exists", CollectionName);

                var collections = await _client.ListCollectionsAsync();
                var collectionExists = collections.Contains(CollectionName);

                if (!collectionExists)
                {
                    _logger.LogInformation("Creating collection '{CollectionName}'", CollectionName);

                    var vectorParams = new VectorParams
                    {
                        Size = 384,
                        Distance = Distance.Cosine
                    };

                    await _client.CreateCollectionAsync(CollectionName, vectorParams);
                    _logger.LogInformation("Successfully created Qdrant collection: {CollectionName}", CollectionName);
                }
                else
                {
                    _logger.LogInformation("Collection '{CollectionName}' already exists", CollectionName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure Qdrant collection exists");
                throw;
            }
        }

        public async Task<Guid> InsertVectorAsync(float[] embedding, int contractId, int chunkIndex, string chunkText)
        {
            try
            {
                await EnsureInitializedAsync();

                var pointId = Guid.NewGuid();
                var point = new PointStruct
                {
                    Id = new PointId { Uuid = pointId.ToString() },
                    Vectors = embedding,
                    Payload =
                    {
                        ["contract_id"] = contractId,
                        ["chunk_index"] = chunkIndex,
                        ["chunk_text"] = chunkText,
                        ["created_at"] = DateTime.UtcNow.ToString("O")
                    }
                };

                await _client.UpsertAsync(CollectionName, new[] { point });
                _logger.LogDebug("Inserted vector for contract {ContractId}, chunk {ChunkIndex}", contractId, chunkIndex);

                return pointId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert vector for contract {ContractId}, chunk {ChunkIndex}", contractId, chunkIndex);
                throw;
            }
        }

        public async Task<List<VectorSearchResult>> SearchSimilarAsync(float[] queryEmbedding, int limit = 10, float scoreThreshold = 0.5f)
        {
            try
            {
                await EnsureInitializedAsync();

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
                    ChunkIndex = (int)point.Payload["chunk_index"].IntegerValue,
                    ChunkText = point.Payload.ContainsKey("chunk_text")
                  ? point.Payload["chunk_text"].StringValue
                  : string.Empty
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search similar vectors");
                throw;
            }
        }

        public async Task DeleteVectorsByContractAsync(int contractId)
        {
            try
            {
                await EnsureInitializedAsync();

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
                _logger.LogInformation("Deleted vectors for contract {ContractId}", contractId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete vectors for contract {ContractId}", contractId);
                throw;
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}