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

                // 1) Get embeddings in batch (MiniLM → 384 dims each)
                var vectors = await _embeddings.EmbedBatchAsync(chunks);

                // 2) Upsert to Qdrant + stage DB rows
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
                using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
                engine.DefaultPageSegMode = PageSegMode.Auto;

                var sb = new System.Text.StringBuilder();

                foreach (Page page in doc.GetPages())
                {
                    string pageText = page.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        sb.AppendLine(pageText);
                    } 

                    var images = page.GetImages().ToList();

                    if (images.Count > 0)
                    {
                        _logger.LogInformation("Running OCR on {Count} images for page {PageNumber}", images.Count, page.Number);

                        using var ms = new MemoryStream();
                        foreach (var img in images)
                        {
                            using var image = Image.Load<Rgba32>(img.RawBytes);
                            ms.SetLength(0);
                            image.Save(ms, new PngEncoder());
                            ms.Position = 0;
                            ms.Seek(0, SeekOrigin.Begin);
                            using var pix = Pix.LoadFromMemory(ms.ToArray());
                            using var pageOcr = engine.Process(pix);
                            var cleanText = Regex.Replace(pageOcr.GetText(), @"\s+", " ").Trim();
                            if (!string.IsNullOrWhiteSpace(cleanText))
                                sb.AppendLine(cleanText);
                        }
                    }
                }
                var result = sb.ToString().Trim();
                if (string.IsNullOrWhiteSpace(result))
                    _logger.LogWarning("No readable text or OCR output found in {FilePath}", filePath);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from PDF: {FilePath}", filePath);
                throw;
            }
        }

        private List<string> ChunkText(string text, int maxSize = 600, int overlap = 100)
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
                var sentenceWithSpace = sentence + " ";

                if (current.Length + sentenceWithSpace.Length > maxSize && current.Length > 0)
                {
                    // save current chunk
                    var chunk = current.ToString().Trim();
                    chunks.Add(chunk);

                    // get overlap tail
                    var overlapText = chunk.Length > overlap
                        ? chunk.Substring(chunk.Length - overlap)
                        : chunk;

                    // start new chunk with overlap
                    current.Clear();
                    current.Append(overlapText);
                }

                current.Append(sentenceWithSpace);
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
            }

            return chunks;
        }

    }
}