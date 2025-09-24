using System.Text.RegularExpressions;
using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using CMPS4110_NorthOaksProj.Models.Contracts;
using UglyToad.PdfPig;
using Tesseract;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;
using Page = UglyToad.PdfPig.Content.Page;
using System.ComponentModel.DataAnnotations;


namespace CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing
{
    public class DocumentProcessingService : IDocumentProcessingService
    {
        private readonly IQdrantService _qdrantService;
        private readonly DataContext _context;
        private readonly ILogger<DocumentProcessingService> _logger;
        private ChunkTokenizer _tokenizer;
        private InferenceSession _onnxSession;

        public DocumentProcessingService(IQdrantService qdrantService, DataContext context, ILogger<DocumentProcessingService> logger)
        {
            _qdrantService = qdrantService;
            _context = context;
            _logger = logger;

            var modelPath = Path.Combine(AppContext.BaseDirectory, "Resources", "text-embedding-3-small.onnx");
            _onnxSession = new InferenceSession(modelPath);
            var vocabPath = Path.Combine(AppContext.BaseDirectory, "Resources", "all-MiniLM-L6-v2-onnx", "vocab.txt");
            _tokenizer = new ChunkTokenizer(vocabPath);

        }

        public async Task ProcessDocumentAsync(int contractId, string filePath)
        {
            try
            {
                var text = ExtractText(filePath);
                var chunks = ChunkText(text);

                var embeddings = new List<ContractEmbedding>();

                foreach (var (chunkText, index) in chunks.Select((text, i) => (text, i)))
                {
                    try
                    {
                        var embedding = GenerateEmbedding(chunkText);
                        var pointId = await _qdrantService.InsertVectorAsync(embedding, contractId, index);

                        embeddings.Add(new ContractEmbedding
                        {
                            ContractId = contractId,
                            ChunkText = chunkText,
                            ChunkIndex = index,
                            QdrantPointId = pointId
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing chunk {Index} for contract {ContractId}", index, contractId);
                    }
                }
                _context.ContractEmbeddings.AddRange(embeddings);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully processed document for contract {ContractId} with {ChunkCount} chunks",
                        contractId, embeddings.Count);
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
                var tessPath = GetTessDataPath();
                using var engine = new TesseractEngine(tessPath, "eng", EngineMode.Default);


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
            var inputIds = _tokenizer.Tokenize(text);
            var attentionMask = inputIds.Select(id => id == 0 ? 0L : 1L).ToArray();

            var inputIdsTensor = new DenseTensor<long>(new[] { 1, inputIds.Length });
            var attentionMaskTensor = new DenseTensor<long>(new[] { 1, inputIds.Length });
            for (int i = 0; i < inputIds.Length; i++)
            {
                inputIdsTensor[0, i] = inputIds[i];
                attentionMaskTensor[0, i] = attentionMask[i];
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
            };

            using var results = _onnxSession.Run(inputs);
            var output = results.First().AsTensor<float>();

            int seqLen = output.Dimensions[1];
            int dim = output.Dimensions[2];
            var embedding = new float[dim];

            for (int i = 0; i < dim; i++)
            {
                float sum = 0f;
                for (int j = 0; j < seqLen; j++)
                    sum += output[0, j, i];
                embedding[i] = sum / seqLen;
            }

            return embedding;
        }

        private string GetTessDataPath()
        {
            var tessPath = Path.Combine(AppContext.BaseDirectory, "Resources", "tessdata");

            if (!Directory.Exists(tessPath))
            {
                throw new DirectoryNotFoundException(
                    $"Tessdata folder not found at {tessPath}. " +
                    $"Make sure eng.traineddata is copied to output.");
            }

            return tessPath;
        }
    }
}
