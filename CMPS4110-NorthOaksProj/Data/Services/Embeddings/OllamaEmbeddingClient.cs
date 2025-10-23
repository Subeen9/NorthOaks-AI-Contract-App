using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace CMPS4110_NorthOaksProj.Data.Services.Embeddings
{
    public sealed class OllamaOptions
    {
        public string BaseUrl { get; set; } = "http://localhost:11434";
        public string Model { get; set; } = "nomic-embed-text:latest";
        public string GenerationModel { get; set; } = "qwen3:4b";
        public int VectorDimension { get; set; } = 768;
    }

    public interface IEmbeddingClient
    {
        Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
        Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);
    }

    public sealed class OllamaEmbeddingClient : IEmbeddingClient
    {
        private readonly HttpClient _http;
        private readonly string _model;
        private readonly ILogger<OllamaEmbeddingClient> _logger;

        public OllamaEmbeddingClient(
            HttpClient http,
            IOptions<OllamaOptions> opts,
            ILogger<OllamaEmbeddingClient> logger)
        {
            _http = http;
            _model = opts.Value.Model ?? "nomic-embed-text:latest";
            _logger = logger;
        }

        // Ollama's actual API format
        private sealed class EmbedReq
        {
            public string model { get; set; } = default!;
            public string prompt { get; set; } = default!;  // Changed: single string only
        }

        private sealed class EmbedRes
        {
            public string model { get; set; } = default!;
            public List<float> embedding { get; set; } = default!;  // Changed: single embedding
        }

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var payload = new EmbedReq { model = _model, prompt = text };

            using var res = await _http.PostAsJsonAsync("/api/embeddings", payload, ct);

            if (!res.IsSuccessStatusCode)
            {
                var errorContent = await res.Content.ReadAsStringAsync(ct);
                _logger.LogError("Ollama API error: {StatusCode} - {Error}", res.StatusCode, errorContent);
                throw new HttpRequestException(
                    $"Ollama API error: {res.StatusCode} - {errorContent}");
            }

            var data = await res.Content.ReadFromJsonAsync<EmbedRes>(cancellationToken: ct)
                       ?? throw new InvalidOperationException("Empty embeddings response");

            return data.embedding.ToArray();
        }

        public async Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
        {
            var textArray = texts.ToArray();
            _logger.LogInformation("Embedding batch of {Count} texts", textArray.Length);

            var results = new List<float[]>();

            // Ollama doesn't support batch - do sequential calls
            for (int i = 0; i < textArray.Length; i++)
            {
                try
                {
                    var embedding = await EmbedAsync(textArray[i], ct);
                    results.Add(embedding);

                    if (i % 10 == 0)
                    {
                        _logger.LogDebug("Embedded {Current}/{Total} texts", i + 1, textArray.Length);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to embed text at index {Index}", i);
                    throw;
                }
            }

            return results.ToArray();
        }
    }
}