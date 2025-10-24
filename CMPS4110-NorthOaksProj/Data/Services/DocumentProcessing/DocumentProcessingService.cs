using System.Text.RegularExpressions;
using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using CMPS4110_NorthOaksProj.Models.Contracts;
using UglyToad.PdfPig;
using Tesseract;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using CMPS4110_NorthOaksProj.Data.Services.Embeddings;
using Page = UglyToad.PdfPig.Content.Page;

namespace CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing
{
    public class DocumentProcessingService : IDocumentProcessingService
    {
        private readonly IQdrantService _qdrantService;
        private readonly DataContext _context;
        private readonly ILogger<DocumentProcessingService> _logger;
        private readonly IEmbeddingClient _embeddings;

        public DocumentProcessingService(
            IQdrantService qdrantService,
            DataContext context,
            ILogger<DocumentProcessingService> logger,
            IEmbeddingClient embeddings)
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

                // Get embeddings in batch (MiniLM → 384 dims each)
                var vectors = await _embeddings.EmbedBatchAsync(chunks);

                //  Upsert to Qdrant + stage DB rows
                var toInsert = new List<ContractEmbedding>(chunks.Count);

                for (var i = 0; i < chunks.Count; i++)
                {
                    try
                    {
                        var pointId = await _qdrantService.InsertVectorAsync(vectors[i], contractId, i, chunks[i]);

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

                // Try text layer first
                var text = string.Join("\n",
                    doc.GetPages()
                       .Select(p => p.Text)
                       .Where(t => !string.IsNullOrWhiteSpace(t)));

                if (!string.IsNullOrWhiteSpace(text))
                    return text;

                _logger.LogInformation("No text layer found in {FilePath}, falling back to OCR", filePath);

                var sb = new System.Text.StringBuilder();
                using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);

                foreach (Page page in doc.GetPages())
                {
                    var images = page.GetImages().ToList();
                    if (images.Count == 0)
                    {
                        _logger.LogInformation("No embedded images on page {PageNumber}, skipping OCR", page.Number);
                        continue;
                    }

                    foreach (var img in images)
                    {
                        using var image = Image.Load<Rgba32>(img.RawBytes);
                        using var ms = new MemoryStream();
                        image.Save(ms, new PngEncoder());
                        ms.Seek(0, SeekOrigin.Begin);
                        using var pix = Pix.LoadFromMemory(ms.ToArray());
                        using var pageOcr = engine.Process(pix);
                        sb.AppendLine(pageOcr.GetText());
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from PDF: {FilePath}", filePath);
                throw;
            }
        }

        private List<string> ChunkText(string text, int maxSize = 600)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            var chunks = new List<string>();
            var current = new System.Text.StringBuilder();

            foreach (var sentence in sentences)
            {

                if (current.Length + sentence.Length + 1 > maxSize && current.Length > 0)
                {

                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                }

                current.Append(sentence + " ");
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
            }

            return chunks;
        
        }

    }
}