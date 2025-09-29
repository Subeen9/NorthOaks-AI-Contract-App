using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CMPS4110_NorthOaksProj.Data.Services.Embeddings;

namespace CMPS4110_NorthOaksProj.Data.Services.Generation
{
    public sealed class OllamaGenerationClient : IOllamaGenerationClient
    {
        private readonly HttpClient _http;
        private readonly string _model;
        private readonly ILogger<OllamaGenerationClient> _logger;

        public OllamaGenerationClient(
            HttpClient http,
            IOptions<OllamaOptions> opts,
            ILogger<OllamaGenerationClient> logger)
        {
            _http = http;
            _model = opts.Value.GenerationModel ?? "llama3.2";
            _logger = logger;
        }



        public async Task<string> GenerateAsync(
            string prompt,
            string? systemPrompt = null,
            CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }
}
