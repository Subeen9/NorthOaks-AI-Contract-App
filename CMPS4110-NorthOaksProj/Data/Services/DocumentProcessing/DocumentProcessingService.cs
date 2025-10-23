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
                //Extract and preprocess text
                var text = ExtractText(filePath);
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("Empty text extracted from contract {ContractId}", contractId);
                    return;
                }

                // Chunk text semantically (smart clause-aware)
                var chunks = ChunkText(text);
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

                // Insert to Qdrant + stage DB rows
                var toInsert = new List<ContractEmbedding>(chunks.Count);

                for (var i = 0; i < chunks.Count; i++)
                {
                    try
                    {
                        var pointId = await _qdrantService.InsertVectorAsync(vectors[i], contractId, chunks[i].ChunkIndex, chunks[i].ChunkText);

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

                //  Commit once
                if (toInsert.Count > 0)
                {
                    _context.ContractEmbeddings.AddRange(toInsert);
                    await _context.SaveChangesAsync();
                }

                _logger.LogInformation(
                    " Processed document for contract {ContractId} with {ChunkCount} chunks",
                    contractId, toInsert.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Failed to process document for contract {ContractId}", contractId);
                throw;
            }
        }

        //  Extract text or OCR fallback
        private string ExtractText(string filePath)
        {
            try
            {
                using var doc = PdfDocument.Open(filePath);

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

        //  Text normalization and cleanup
        private string NormalizeText(string input)
        {
            input = input.ToLowerInvariant();
            input = Regex.Replace(input, @"page\s*\d+", "", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"confidential", "", RegexOptions.IgnoreCase);
            input = Regex.Replace(input, @"\s{2,}", " ");
            return input.Trim();
        }

        // Semantic-aware + adaptive chunking
        private List<ContractChunk> ChunkText(string text, int minLen = 300, int maxLen = 800, double overlapRatio = 0.15)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<ContractChunk>();

            text = NormalizeText(text);

            //  Split by numbered clauses like "1. TERM", "2. PAYMENT", etc.
            var sections = Regex.Split(text, @"(?=^\d+\.\s*[A-Z])", RegexOptions.Multiline)
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToList();

            var allChunks = new List<ContractChunk>();
            int globalIndex = 0;

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
                        SectionTitle = sectionTitle 
                    });
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
                    chunks.Add(current.ToString().Trim());
                    current.Clear();

                    // smart overlap (retain 15% of previous chunk)
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
    }

   
    internal class ContractChunk
    {
        public string ChunkText { get; set; } = "";
        public int ChunkIndex { get; set; }
        public string? SectionTitle { get; set; }
    }
}
