namespace CMPS4110_NorthOaksProj.Data.Services.Generation
{
    public interface IOllamaGenerationClient
    {
        Task<string> GenerateAsync(string prompt, string? systemPrompt = null, CancellationToken ct = default);
    }
}