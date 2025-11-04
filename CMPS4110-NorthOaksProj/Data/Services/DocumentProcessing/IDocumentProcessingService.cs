namespace CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing
{
    public interface IDocumentProcessingService
    {
        Task ProcessDocumentAsync(int contractId, string filePath, Func<int, string, Task>? progressCallback = null, CancellationToken cancellationToken = default);
    }
}
