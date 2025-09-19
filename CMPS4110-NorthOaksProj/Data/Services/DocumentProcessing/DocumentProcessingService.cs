
using System.Text.RegularExpressions;
using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using CMPS4110_NorthOaksProj.Models.Contracts;
using UglyToad.PdfPig;
using CMPS4110_NorthOaksProj.Data.Services.Embeddings;

namespace CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing
{
    public class DocumentProcessingService : IDocumentProcessingService
    {
        private readonly IQdrantService _qdrantService;
        private readonly DataContext _context;
        private readonly ILogger<DocumentProcessingService> _logger;
        private readonly IEmbeddingClient _embeddings;   // NEW

        public DocumentProcessingService(
            IQdrantService qdrantService,
            DataContext context,
            ILogger<DocumentProcessingService> logger,
            IEmbeddingClient embeddings)                  // NEW
        {
            _qdrantService = qdrantService;
            _context = context;
            _logger = logger;
            _embeddings = embeddings;
        }

        public async Task ProcessDocumentAsync(int contractId, string filePath)
        {
            try
            {
                var text = ExtractText(filePath);
                var chunks = ChunkText(text);

                if (chunks.Count == 0)
                {
                    _logger.LogWarning("No chunks produced for contract {ContractId}", contractId);
                    return;
                }

                // 1) Get embeddings in batch (MiniLM → 384 dims each)
                var vectors = await _embeddings.EmbedBatchAsync(chunks);

                // 2) Upsert to Qdrant + stage DB rows
                var toInsert = new List<ContractEmbedding>(chunks.Count);

                for (var i = 0; i < chunks.Count; i++)
                {
                    try
                    {
                        var pointId = await _qdrantService.InsertVectorAsync(vectors[i], contractId, i);

                        toInsert.Add(new ContractEmbedding
                        {
                            ContractId = contractId,
                            ChunkText = chunks[i],
                            ChunkIndex = i,
                            QdrantPointId = pointId
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing chunk {Index} for contract {ContractId}", i, contractId);
                    }
                }

                // 3) Commit once
                if (toInsert.Count > 0)
                {
                    _context.ContractEmbeddings.AddRange(toInsert);
                    await _context.SaveChangesAsync();
                }

                _logger.LogInformation(
                    "Successfully processed document for contract {ContractId} with {ChunkCount} chunks",
                    contractId, toInsert.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process document for contract {ContractId}", contractId);
                throw;
            }
        }

        private string ExtractText(string filePath)
        {
            try
            {
                using var doc = PdfDocument.Open(filePath);
                return string.Join("\n", doc.GetPages().Select(p => p.Text));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from PDF: {FilePath}", filePath);
                throw;
            }
        }

        private List<string> ChunkText(string text, int maxSize = 800)
        {
            var sentences = Regex.Split(text, @"(?<=[.!?])\s+").Where(s => !string.IsNullOrWhiteSpace(s));
            var chunks = new List<string>();
            var current = "";

            foreach (var sentence in sentences)
            {
                if (current.Length + sentence.Length > maxSize && !string.IsNullOrEmpty(current))
                {
                    chunks.Add(current.Trim());
                    current = "";
                }
                current += sentence + " ";
            }

            if (!string.IsNullOrEmpty(current))
                chunks.Add(current.Trim());

            return chunks;
        }
    }
}
