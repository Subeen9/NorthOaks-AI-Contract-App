using System.Text.RegularExpressions;
using CMPS4110_NorthOaksProj.Data.Services.Embeddings;
using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using CMPS4110_NorthOaksProj.Hubs;
using CMPS4110_NorthOaksProj.Models.Contracts;
using Microsoft.AspNetCore.SignalR;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Tesseract;
using UglyToad.PdfPig;
using Page = UglyToad.PdfPig.Content.Page;


namespace CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing
{
    public class DocumentProcessingService : IDocumentProcessingService
    {
        private readonly IQdrantService _qdrantService;
        private readonly DataContext _context;
        private readonly ILogger<DocumentProcessingService> _logger;
        private readonly IEmbeddingClient _embeddings;
        private readonly IHubContext<ProcessingHub> _hubContext;

        public DocumentProcessingService(
            IQdrantService qdrantService,
            DataContext context,
            ILogger<DocumentProcessingService> logger,
            IEmbeddingClient embeddings,
            IHubContext<ProcessingHub> hubContext)
        {
            _qdrantService = qdrantService;
            _context = context;
            _logger = logger;
            _embeddings = embeddings;
            _hubContext = hubContext;
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
                            ChunkText = chunks[i].ChunkText,
                            ChunkIndex = chunks[i].ChunkIndex,
                            QdrantPointId = pointId
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error embedding chunk {Index} for contract {ContractId}", i, contractId);
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
                    " Processed document for contract {ContractId} with {ChunkCount} chunks",
                    contractId, toInsert.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process document for contract {ContractId}", contractId);
                // bubble up so caller can send error notifications if needed
                throw;
            }
        }

        private List<PageText> ExtractTextWithPages(string filePath)
        {
            try
            {
                using var doc = PdfDocument.Open(filePath);
                var pageTexts = new List<PageText>();

                foreach (var page in doc.GetPages())
                {
                    string text = page.Text;

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        var images = page.GetImages().ToList();
                        if (images.Any())
                        {
                            _logger.LogInformation(
                                "Page {PageNum} has no text layer but has {ImageCount} image(s), performing OCR",
                                page.Number, images.Count);
                            text = OcrPage(page);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        pageTexts.Add(new PageText
                        {
                            PageNumber = page.Number,
                            Text = text
                        });
                    }
                }

                _logger.LogInformation("Extracted text from {PageCount} pages", pageTexts.Count);
                return pageTexts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from PDF: {FilePath}", filePath);
                throw;
            }
        }

        private string OcrPage(Page page)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);

                var images = page.GetImages().ToList();
                foreach (var img in images)
                {
                    try
                    {
                        byte[] imageBytes;
                        if (img.TryGetPng(out var pngBytes))
                        {
                            imageBytes = pngBytes;
                        }
                        else
                        {
                            imageBytes = img.RawBytes.ToArray();
                        }

                        using var image = Image.Load<Rgba32>(imageBytes);
                        using var ms = new MemoryStream();
                        image.Save(ms, new PngEncoder());
                        ms.Seek(0, SeekOrigin.Begin);

                        using var pix = Pix.LoadFromMemory(ms.ToArray());
                        using var pageOcr = engine.Process(pix);
                        var ocrText = pageOcr.GetText();

                        if (!string.IsNullOrWhiteSpace(ocrText))
                        {
                            sb.AppendLine(ocrText);
                        }
                    }
                    catch (Exception imgEx)
                    {
                        _logger.LogWarning(imgEx, "Failed to process individual image, continuing with next");
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR failed for page");
                return string.Empty;
            }
        }

        private string NormalizeText(string input)
        {
            input = input.ToLowerInvariant();
            input = Regex.Replace(input, @"page\s*\d+", "", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"confidential", "", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\s{2,}", " ");
            return input.Trim();
        }

        private List<ContractChunk> ChunkTextWithPages(
            List<PageText> pageTexts,
            int minLen = 300,
            int maxLen = 800,
            double overlapRatio = 0.15)
        {
            var allChunks = new List<ContractChunk>();
            int globalIndex = 0;

            foreach (var pageText in pageTexts)
            {
                var normalizedText = NormalizeText(pageText.Text);

                if (string.IsNullOrWhiteSpace(normalizedText))
                    continue;

                var sections = Regex.Split(normalizedText, @"(?=^\d+\.\s*[A-Z])", RegexOptions.Multiline)
                                    .Where(s => !string.IsNullOrWhiteSpace(s))
                                    .ToList();

                foreach (var sectionText in sections)
                {
                    var match = Regex.Match(sectionText, @"^\d+\.\s*([A-Za-z\s]+)");
                    string sectionTitle = match.Success ? match.Groups[1].Value.Trim() : "General";

                    var adaptiveChunks = AdaptiveChunkSection(sectionText, minLen, maxLen, overlapRatio);

                    foreach (var chunkText in adaptiveChunks)
                    {
                        allChunks.Add(new ContractChunk
                        {
                            ChunkText = chunkText,
                            ChunkIndex = globalIndex++,
                            SectionTitle = sectionTitle,
                            PageNumber = pageText.PageNumber
                        });
                    }
                }
            }

            return allChunks;
        }

        private List<string> AdaptiveChunkSection(string section, int minLen, int maxLen, double overlapRatio)
        {
            var sentences = Regex.Split(section, @"(?<=[.!?])\s+")
                                 .Where(s => !string.IsNullOrWhiteSpace(s))
                                 .ToList();

            var chunks = new List<string>();
            var current = new System.Text.StringBuilder();

            foreach (var sentence in sentences)
            {
                if (current.Length + sentence.Length > maxLen)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();

                    var prev = chunks.Last();
                    int overlapLen = (int)(prev.Length * overlapRatio);
                    string overlapText = prev.Length > overlapLen
                        ? prev.Substring(prev.Length - overlapLen)
                        : prev;
                    current.Append(overlapText).Append(" ");
                }

                current.Append(sentence).Append(" ");
            }

            if (current.Length > 0)
                chunks.Add(current.ToString().Trim());

            return chunks;
        }

        internal class ContractChunk
        {
            public string ChunkText { get; set; } = "";
            public int ChunkIndex { get; set; }
            public string? SectionTitle { get; set; }
            public int PageNumber { get; set; }
        }

        internal class PageText
        {
            public int PageNumber { get; set; }
            public string Text { get; set; } = "";
        }
    }
}