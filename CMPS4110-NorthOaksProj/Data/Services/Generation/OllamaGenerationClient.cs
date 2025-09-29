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

        private sealed class GenerateRequest
        {
            public string model { get; set; } = default!;
            public string prompt { get; set; } = default!;
            public string? system { get; set; }
            public bool stream { get; set; } = false;
            public GenerateOptions? options { get; set; }
        }

        private sealed class GenerateOptions
        {
            public double temperature { get; set; } = 0.7;
            public int num_predict { get; set; } = 500;
        }

        private sealed class GenerateResponse
        {
            public string model { get; set; } = default!;
            public string response { get; set; } = default!;
            public bool done { get; set; }
        }

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
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Prompt cannot be empty", nameof(prompt));

            // build request
            var request = new GenerateRequest
            {
                model = _model,
                prompt = prompt,
                system = systemPrompt,
                stream = false,
                options = new GenerateOptions
                {
                    temperature = 0.7,
                    num_predict = 500
                }
            };
            try
            {
                _logger.LogDebug("Sending generation request to Ollama with model: {Model}", _model);

                // make HTTP call
                using var response = await _http.PostAsJsonAsync("/api/generate", request, ct);
                response.EnsureSuccessStatusCode();

                // deserialize response
                var result = await response.Content.ReadFromJsonAsync<GenerateResponse>(cancellationToken: ct)
                    ?? throw new InvalidOperationException("Empty generation response from Ollama");

                _logger.LogDebug("Received response from Ollama: {ResponseLength} characters", result.response.Length);

                return result.response;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error calling Ollama generation API");
                throw new InvalidOperationException("Failed to connect to Ollama. Make sure Ollama is running and the model is available.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating text with Ollama");
                throw;
            }
        }
    
    }
}
