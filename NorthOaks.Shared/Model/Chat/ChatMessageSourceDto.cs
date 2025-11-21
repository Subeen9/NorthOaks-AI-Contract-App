namespace NorthOaks.Shared.Model.Chat
{
    public class ChatMessageSourceDto
    {
        public int ContractId { get; set; }
        public string OriginalChunkText { get; set; } = string.Empty;
        public string ChunkText { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public string? SectionTitle { get; set; }
        public int PageNumber { get; set; }
        public double SimilarityScore { get; set; }
    }
}