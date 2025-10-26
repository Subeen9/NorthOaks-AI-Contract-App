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
                // 1️⃣ Extract text WITH page numbers
                var pageTexts = ExtractTextWithPages(filePath);

                if (pageTexts.Count == 0)
                {
                    _logger.LogWarning("Empty text extracted from contract {ContractId}", contractId);
                    return;
                }

                // 2️⃣ Chunk text WITH page awareness
                var chunks = ChunkTextWithPages(pageTexts);

                if (chunks.Count == 0)
                {
                    _logger.LogWarning("No chunks produced for contract {ContractId}", contractId);
                    return;
                }

                // 3️⃣ Get embeddings in batch
                var chunkTexts = chunks.Select(c => c.ChunkText).ToList();
                var vectors = await _embeddings.EmbedBatchAsync(chunkTexts);
                vectors = EmbeddingUtils.NormalizeBatch(vectors);
                EmbeddingUtils.PrintNormStats(vectors);

                // 4️⃣ Insert to Qdrant + stage DB rows
                var toInsert = new List<ContractEmbedding>(chunks.Count);

                for (var i = 0; i < chunks.Count; i++)
                {
                    try
                    {
                        // ✅ Pass page number to QDrant
                        var pointId = await _qdrantService.InsertVectorAsync(
                            vectors[i],
                            contractId,
                            chunks[i].ChunkIndex,
                            chunks[i].ChunkText,
                            chunks[i].PageNumber  // ✅ NEW: Include page number
                        );

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
                }

                if (toInsert.Count > 0)
                {
                    _context.ContractEmbeddings.AddRange(toInsert);
                    await _context.SaveChangesAsync();
                }

                _logger.LogInformation(
                    "✅ Processed document for contract {ContractId} with {ChunkCount} chunks",
                    contractId, toInsert.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to process document for contract {ContractId}", contractId);
                throw;
            }
        }

        // ✅ NEW: Extract text WITH page numbers
        private List<PageText> ExtractTextWithPages(string filePath)
        {
            try
            {
                using var doc = PdfDocument.Open(filePath);
                var pageTexts = new List<PageText>();

                foreach (var page in doc.GetPages())
                {
                    var text = page.Text;

                    // If no text layer, try OCR
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _logger.LogInformation("No text layer on page {PageNum}, falling back to OCR", page.Number);
                        text = OcrPage(page);
                    }

                    // Only add pages with actual text
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        pageTexts.Add(new PageText
                        {
                            PageNumber = page.Number,
                            Text = text
                        });
                    }
                }

                return pageTexts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from PDF: {FilePath}", filePath);
                throw;
            }
        }

        // ✅ NEW: Separate OCR method
        private string OcrPage(Page page)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);

                var images = page.GetImages().ToList();
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

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR failed for page");
                return string.Empty;
            }
        }

        // Text normalization and cleanup
        private string NormalizeText(string input)
        {
            input = input.ToLowerInvariant();
            input = Regex.Replace(input, @"page\s*\d+", "", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"confidential", "", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\s{2,}", " ");
            return input.Trim();
        }

        // ✅ UPDATED: Chunk text WITH page awareness
        private List<ContractChunk> ChunkTextWithPages(
            List<PageText> pageTexts,
            int minLen = 300,
            int maxLen = 800,
            double overlapRatio = 0.15)
        {
            var allChunks = new List<ContractChunk>();
            int globalIndex = 0;

            // Process each page separately to preserve page numbers
            foreach (var pageText in pageTexts)
            {
                var normalizedText = NormalizeText(pageText.Text);

                if (string.IsNullOrWhiteSpace(normalizedText))
                    continue;

                // Split by numbered clauses like "1. TERM", "2. PAYMENT", etc.
                var sections = Regex.Split(normalizedText, @"(?=^\d+\.\s*[A-Z])", RegexOptions.Multiline)
                                    .Where(s => !string.IsNullOrWhiteSpace(s))
                                    .ToList();

                foreach (var sectionText in sections)
                {
                    var match = Regex.Match(sectionText, @"^\d+\.\s*([A-Za-z\s]+)");
                    string sectionTitle = match.Success ? match.Groups[1].Value.Trim() : "General";

                    // Break large sections adaptively
                    var adaptiveChunks = AdaptiveChunkSection(sectionText, minLen, maxLen, overlapRatio);

                    foreach (var chunkText in adaptiveChunks)
                    {
                        allChunks.Add(new ContractChunk
                        {
                            ChunkText = chunkText,
                            ChunkIndex = globalIndex++,
                            SectionTitle = sectionTitle,
                            PageNumber = pageText.PageNumber  // ✅ CAPTURE PAGE NUMBER
                        });
                    }
                }
            }

            return allChunks;
        }

        // Adaptive sentence-level chunking with smart overlap
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
                    // Add current chunk
                    chunks.Add(current.ToString().Trim());
                    current.Clear();

                    // Smart overlap (retain 15% of previous chunk)
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

        // ✅ Internal classes
        internal class ContractChunk
        {
            public string ChunkText { get; set; } = "";
            public int ChunkIndex { get; set; }
            public string? SectionTitle { get; set; }
            public int PageNumber { get; set; }  // ✅ Added page tracking
        }

        // ✅ NEW: PageText class
        internal class PageText
        {
            public int PageNumber { get; set; }
            public string Text { get; set; } = "";
        }
    }
}