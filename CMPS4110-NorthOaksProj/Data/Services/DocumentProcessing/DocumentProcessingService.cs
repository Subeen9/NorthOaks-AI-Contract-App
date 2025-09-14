using System.Text.RegularExpressions;
using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using CMPS4110_NorthOaksProj.Models.Contracts;
using UglyToad.PdfPig;


namespace CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing
{


        public class DocumentProcessingService : IDocumentProcessingService
        {
            private readonly IQdrantService _qdrantService;
            private readonly DataContext _context;
            private readonly ILogger<DocumentProcessingService> _logger;

            public DocumentProcessingService(IQdrantService qdrantService, DataContext context, ILogger<DocumentProcessingService> logger)
            {
                _qdrantService = qdrantService;
                _context = context;
                _logger = logger;
            }

            public async Task ProcessDocumentAsync(int contractId, string filePath)
            {
                var text = ExtractText(filePath);
                var chunks = ChunkText(text);

                foreach (var (chunkText, index) in chunks.Select((text, i) => (text, i)))
                {
                    var embedding = GenerateEmbedding(chunkText);
                    var pointId = await _qdrantService.InsertVectorAsync(embedding, contractId, index);

                    _context.ContractEmbeddings.Add(new ContractEmbedding
                    {
                        ContractId = contractId,
                        ChunkText = chunkText,
                        ChunkIndex = index,
                        QdrantPointId = pointId
                    });
                }

                await _context.SaveChangesAsync();
            }

            private string ExtractText(string filePath)
            {
                using var doc = PdfDocument.Open(filePath);
                return string.Join("\n", doc.GetPages().Select(p => p.Text));
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

            private float[] GenerateEmbedding(string text)
            {
                // Dummy embedding-- needs to be replaced by real model
                var random = new Random(text.GetHashCode());
                return Enumerable.Range(0, 384).Select(_ => (float)(random.NextDouble() - 0.5) * 2).ToArray();
            }
        }
    
}
