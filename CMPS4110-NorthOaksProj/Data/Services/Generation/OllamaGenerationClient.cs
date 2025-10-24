using CMPS4110_NorthOaksProj.Data.Services.Embeddings;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

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

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var request = new GenerateRequest
            {
                model = _model,
                prompt = prompt,
                system = systemPrompt,
                stream = true,   // IMPORTANT: streaming enabled
                options = new GenerateOptions
                {
                    temperature = 0.6,
                    num_predict = 400
                }
            };

            try
            {
                _logger.LogInformation("Starting generation: model={Model}", _model);

                using var response = await _http.PostAsJsonAsync("/api/generate", request, ct);
                _logger.LogInformation("Request completed in {Ms}ms, status: {Status}",
                    sw.ElapsedMilliseconds, response.StatusCode);

                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);

                var sb = new StringBuilder();

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var chunk = JsonSerializer.Deserialize<GenerateResponse>(line);

                        if (!string.IsNullOrWhiteSpace(chunk?.response))
                            sb.Append(chunk.response);

                        if (chunk?.done == true)
                            break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse chunk: {Line}", line);
                    }
                }

                var finalText = sb.ToString();
                _logger.LogInformation("Generation final: {Len} chars in {Ms}ms",
                    finalText.Length, sw.ElapsedMilliseconds);

                return finalText;
            }
            catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
            {
                _logger.LogError(ex, "Generation failed after {Ms}ms", sw.ElapsedMilliseconds);
                throw;
            }
        }

    }
}
