using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace CMPS4110_NorthOaksProj.Data.Services.Embeddings
{
    public sealed class OllamaOptions
    {
        public string BaseUrl { get; set; } = "http://localhost:11434";
        public string Model { get; set; } = "nomic-embed-text"; // 384 dims
        public string GenerationModel { get; set; } = "llama3.2";
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

        public OllamaEmbeddingClient(HttpClient http, IOptions<OllamaOptions> opts)
        {
            _http = http;
            _model = opts.Value.Model ?? "all-minilm";
        }

        private sealed class EmbedReq
        {
            public string model { get; set; } = default!;
            public object input { get; set; } = default!; // string or string[]
            public bool? stream { get; set; } = false;
        }

        private sealed class EmbedRes
        {
            public string model { get; set; } = default!;
            public List<List<float>> embeddings { get; set; } = default!;
        }

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var payload = new EmbedReq { model = _model, input = text, stream = false };
            using var res = await _http.PostAsJsonAsync("/api/embed", payload, ct);
            res.EnsureSuccessStatusCode();
            var data = await res.Content.ReadFromJsonAsync<EmbedRes>(cancellationToken: ct)
                       ?? throw new InvalidOperationException("Empty embeddings response");
            return data.embeddings[0].ToArray();
        }

        public async Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)
        {
            var arr = texts.ToArray();
            var payload = new EmbedReq { model = _model, input = arr, stream = false };
            using var res = await _http.PostAsJsonAsync("/api/embed", payload, ct);
            res.EnsureSuccessStatusCode();
            var data = await res.Content.ReadFromJsonAsync<EmbedRes>(cancellationToken: ct)
                       ?? throw new InvalidOperationException("Empty embeddings response");
            return data.embeddings.Select(v => v.ToArray()).ToArray();
        }
    }
}
