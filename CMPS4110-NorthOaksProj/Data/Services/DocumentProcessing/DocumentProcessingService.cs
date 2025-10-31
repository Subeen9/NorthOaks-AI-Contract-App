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

        // signature changed to accept cancellation token + progress callback
        public async Task ProcessDocumentAsync(int contractId, string filePath, Func<int, string, Task>? progressCallback = null, CancellationToken cancellationToken = default)
        {
            try
            {
                progressCallback = progressCallback ?? (async (p, m) => { await Task.CompletedTask; });

                // Start
                await progressCallback(5, "Starting document processing...");

                // Extract text
                await progressCallback(10, "Extracting text from PDF...");
                var text = ExtractText(filePath);
                if (cancellationToken.IsCancellationRequested) return;

                // Chunk text
                await progressCallback(30, "Chunking text...");
                var chunks = ChunkText(text);
                if (cancellationToken.IsCancellationRequested) return;

                if (chunks.Count == 0)
                {
                    _logger.LogWarning("No chunks produced for contract {ContractId}", contractId);
                    await progressCallback(100, "No text found.");
                    return;
                }

                // Get embeddings in batch
                await progressCallback(40, $"Generating embeddings for {chunks.Count} chunks...");
                var vectors = await _embeddings.EmbedBatchAsync(chunks);
                if (cancellationToken.IsCancellationRequested) return;

                // Prepare DB rows and upsert to Qdrant
                var toInsert = new List<ContractEmbedding>(chunks.Count);
                for (var i = 0; i < chunks.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        // Insert vector, you already have an InsertVectorAsync returning point id
                        var pointId = await _qdrantService.InsertVectorAsync(vectors[i], contractId, i, chunks[i], -1);

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

                    // progress: map chunk index to percentage between 45 and 95
                    var pct = 45 + (int)((double)(i + 1) / chunks.Count * 50); // 45..95
                    await progressCallback(Math.Min(pct, 95), $"Processed chunk {i + 1} / {chunks.Count}");
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    await progressCallback(-1, "Processing cancelled.");
                    return;
                }

                // Commit DB rows once
                if (toInsert.Count > 0)
                {
                    await progressCallback(96, "Saving embeddings to database...");
                    _context.ContractEmbeddings.AddRange(toInsert);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                await progressCallback(100, "Processing complete.");
                _logger.LogInformation(
                    "Successfully processed document for contract {ContractId} with {ChunkCount} chunks",
                    contractId, toInsert.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process document for contract {ContractId}", contractId);
                // bubble up so caller can send error notifications if needed
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